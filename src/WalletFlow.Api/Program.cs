using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Filters;
using WalletFlow.Api.Middlewares;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.Emails;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Infrastructure;
using WalletFlow.Infrastructure.Emails;
using WalletFlow.Infrastructure.Persistence;

// Prépare l'application web de l'API WalletFlow.
var builder = WebApplication.CreateBuilder(args);

// La durée de vie des services est vérifiée dès le démarrage, dans tous les environnements. Un service gardé pour toute l'API
// qui recevrait un service d'une seule requête garderait l'accès à la base de la première requête, fermé juste après : l'API
// refuse donc de démarrer. Hors du développement, ASP.NET Core ne fait pas cette vérification de lui-même.
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

// La chaîne de connexion vient des secrets (user-secrets en développement, variables
// d'environnement en production), jamais du code ni de Git (SS §7).
var connectionString =
    builder.Configuration.GetConnectionString("WalletFlowDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "La chaîne de connexion « WalletFlowDatabase » est absente. En développement, ajoute-la avec dotnet user-secrets.");
}
builder.Services.AddWalletFlowDatabase(connectionString);

// Une seule instance de l'API par base (voir SingleInstanceGuard) : la garde prend son verrou au démarrage, avant toutes les
// tâches de fond et avant le serveur. Elle doit rester le premier service hébergé branché.
builder.Services.AddSingleton(new SingleInstanceLock(connectionString, SingleInstanceLock.ApiInstanceKey));
builder.Services.AddHostedService<SingleInstanceGuard>();

// L'horloge de l'application : les tests peuvent la remplacer pour vérifier les durées sans attendre.
builder.Services.AddSingleton(TimeProvider.System);

// Chaque réponse de la connexion et de l'inscription dure au moins ce temps : son temps de réponse ne révèle donc pas si un compte utilise l'adresse (SS §2).
// Le réglage est lu au démarrage : une valeur illisible empêche l'API de démarrer.
var minimumDuration = MinimumAnswerDurationFilter.ReadMinimumDuration(builder.Configuration);
builder.Services.AddSingleton(serviceProvider =>
    new MinimumAnswerDurationFilter(minimumDuration, serviceProvider.GetRequiredService<TimeProvider>()));

// Hachage des mots de passe, création des jetons et limites par adresse e-mail (SS §2 et §6).
builder.Services.AddWalletFlowSecurityTools();

// Les clés qui chiffrent les secrets de double authentification viennent des secrets, jamais du code ni de Git (SS §2 et §7).
// Sans clé actuelle, aucun compte avec double authentification ne pourrait utiliser son application : l'API refuse donc de
// démarrer. Les anciennes clés ne servent plus qu'à déchiffrer, le temps que la tâche quotidienne rechiffre tout.
var totpEncryption = TotpEncryptionSettings.Read(builder.Configuration);
builder.Services.AddTotpSecretEncryption(totpEncryption.CurrentKeyId, totpEncryption.CurrentKey, totpEncryption.PreviousKeys);

// File des e-mails préparés en arrière-plan : le temps de réponse des routes ne révèle aucun compte (SS §2).
builder.Services.AddEmailQueue();

// Texte des e-mails envoyés aux utilisateurs.
builder.Services.AddEmailTexts();

// En développement seulement, les e-mails sont écrits dans un dossier local, ignoré par Git, au lieu de partir : même avec des
// réglages SMTP, aucun vrai e-mail ne part d'un poste de développement. Ailleurs, ils partent par le serveur SMTP du prestataire,
// et aucun n'est jamais écrit sur le disque : ses liens à usage unique ouvrent un compte (SS §9). Sans réglages SMTP, l'API refuse
// donc de démarrer hors du développement (voir après la construction de l'application).
var smtpServer = SmtpConfiguration.Read(builder.Configuration);
if (builder.Environment.IsDevelopment())
{
    var emailsFolder = builder.Configuration["LocalEmailsFolder"];
    if (string.IsNullOrWhiteSpace(emailsFolder))
    {
        throw new InvalidOperationException(
            "Le réglage « LocalEmailsFolder » est absent : aucun moyen d'envoyer les e-mails n'est configuré.");
    }

    builder.Services.AddLocalFolderEmails(
        Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, emailsFolder)));
}
else if (smtpServer != null)
{
    builder.Services.AddSmtpEmails(smtpServer);
}
else
{
    builder.Services.AddEmailSenderNotConfigured();
}

// En dehors du développement, l'API ne répond qu'aux noms du site, lus dans l'en-tête Host : « * » répondrait à n'importe quel nom
// qui pointe vers le serveur. Le réglage « AllowedHosts » donne ces noms, séparés par des points-virgules. Sans lui, l'API refuse
// de démarrer.
var allowedHosts = builder.Configuration["AllowedHosts"];
if (!builder.Environment.IsDevelopment()
    && (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts.Split(';').Any(host => host.Trim() == "*")))
{
    throw new InvalidOperationException(
        "Le réglage « AllowedHosts » doit donner les noms du site, séparés par des points-virgules, jamais « * ».");
}

// Une requête sous un autre nom reçoit un 400 vide. La page HTML qu'ASP.NET Core écrit par défaut serait la seule réponse de
// l'API hors JSON, et partirait sans les en-têtes de sécurité : le filtre des noms passe avant eux.
builder.Services.Configure<HostFilteringOptions>(options => options.IncludeFailureMessage = false);

// Adresse publique du site, qui sert à construire les liens envoyés par e-mail (SS §9).
builder.Services.AddSingleton(
    new PublicWebsiteSettings(builder.Configuration["PublicWebsiteAddress"]));

// Le prestataire bancaire Enable Banking : l'identifiant de l'application et le chemin de sa clé privée viennent des secrets
// (user-secrets en développement, variables d'environnement en production), jamais du code ni de Git (SS §7).
// En production, l'API refuse de démarrer sans eux. En développement, les routes des banques répondent 503 en attendant.
var bankProviderSettings = BankProviderSettings.Read(builder.Configuration);
if (bankProviderSettings != null)
{
    if (!File.Exists(bankProviderSettings.PrivateKeyPath))
    {
        throw new InvalidOperationException(
            "La clé privée d'Enable Banking est introuvable au chemin donné par « " + BankProviderSettings.PrivateKeyPathSetting + " ».");
    }

    builder.Services.AddEnableBanking(bankProviderSettings.ApplicationId, File.ReadAllText(bankProviderSettings.PrivateKeyPath));
}
else if (builder.Environment.IsDevelopment())
{
    builder.Services.AddBankProviderNotConfigured();
}
else
{
    throw new InvalidOperationException(
        "Le réglage « " + BankProviderSettings.ApplicationIdSetting + " » ou « " + BankProviderSettings.PrivateKeyPathSetting
        + " » manque : la connexion bancaire n'est pas configurée.");
}
builder.Services.AddBankConnectionTools();

// Cas d'utilisation de la couche Application, avec leurs outils communs, rangés par domaine (voir ApplicationServicesRegistration).
builder.Services.AddWalletFlowUseCases();

// Tâches de fond : nettoyages quotidiens, file d'e-mails, soldes, suggestions, alertes, suppression des comptes et rechiffrement
// des secrets de double authentification (voir ApplicationServicesRegistration).
builder.Services.AddWalletFlowBackgroundTasks();

// Limites de débit des points d'entrée sensibles (SS §6).
builder.Services.AddWalletFlowRateLimiting();

// L'adresse du client derrière un proxy inverse : lue seulement depuis un proxy déclaré (SS §6).
var reverseProxy = ReverseProxyConfiguration.Read(builder.Configuration);
builder.Services.Configure<ForwardedHeadersOptions>(reverseProxy.Apply);

// L'utilisateur est reconnu par son cookie de session, gardée côté serveur (SS §3).
builder.Services
    .AddAuthentication(UserSessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, UserSessionAuthenticationHandler>(UserSessionAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();

// Les clés qui signent le jeton anti-CSRF survivent aux redéploiements : hors du développement, elles sont gardées dans le dossier
// du réglage « DataProtection:KeysFolder », chiffrées avec le certificat de « DataProtection:CertificatePath » et sa clé privée.
// Sans eux, l'API refuse de démarrer (SS §6 et §7).
var keysFolder = DataProtectionConfiguration.ReadKeysFolder(builder.Configuration, builder.Environment.IsDevelopment());
var keysCertificate = DataProtectionConfiguration.ReadCertificate(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddWalletFlowDataProtection(keysFolder, keysCertificate);

// Points de santé pour l'hébergeur : l'API tourne, et la base répond (SS §11).
builder.Services.AddWalletFlowHealthChecks();

// Jeton anti-CSRF fourni par ASP.NET Core (SS §6).
builder.Services.AddAntiforgery(AntiforgeryConfiguration.ApplyOptions);

// Les contrôleurs reçoivent les requêtes HTTP et appellent la couche Application.
// Une requête refusée reçoit toujours le même format d'erreur, sans aucun nom interne du code (SS §10).
// AddControllersWithViews plutôt que AddControllers : le filtre anti-CSRF d'ASP.NET Core en fait partie. L'API n'a aucune vue.
builder.Services
    .AddControllersWithViews(options =>
    {
        // Toute route demande une session et le jeton anti-CSRF, même si on oublie de le demander :
        // une route publique doit le dire avec [AllowAnonymous] et [IgnoreAntiforgeryToken] (SS §4 et §6).
        options.Filters.Add(new AuthorizeFilter());
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());

        // Le refus d'un jeton anti-CSRF absent ou faux dit au site d'en demander un nouveau. Le filtre est ajouté lui-même, et non
        // par son type : ASP.NET Core ne lirait pas son rang, qui doit passer avant celui des erreurs sans raison.
        options.Filters.Add(new CsrfRefusalFilter());

        // Un calcul coûteux refusé parce que le serveur est saturé répond « serveur occupé » (503), dans tous les environnements.
        options.Filters.Add<ServerBusyExceptionFilter>();

        // Une valeur illisible dans l'adresse, comme « amountInCents=abc », reçoit un message en français
        // qui ne recopie jamais ce que le visiteur a écrit (SS §10).
        ApiErrorConfiguration.TranslateBindingMessages(options.ModelBindingMessageProvider);
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = ApiErrorConfiguration.CreateRefusedRequestAnswer;
    })
    .AddJsonOptions(options =>
    {
        options.AllowInputFormatterExceptionMessages = false;

        // Un montant s'écrit comme un nombre JSON, jamais comme un texte (SS §5) : ASP.NET Core accepte « "3000" » par défaut.
        options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    });

// Décrit l'API au format OpenAPI (publié seulement en développement) : chaque route, avec toutes ses réponses.
// La description lit les réglages JSON généraux d'ASP.NET Core, et non ceux des contrôleurs : ils refusent aussi un nombre
// écrit comme un texte, sinon elle annoncerait que « "3000" » est accepté.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
builder.Services.AddOpenApi(OpenApiConfiguration.ApplyOptions);

// Les erreurs sont renvoyées au format « problem details », en français et sans détail technique (SS §10).
// Les clés des champs refusés s'écrivent comme dans le JSON : « newEmail », et non « NewEmail ».
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ApiErrorConfiguration.PrepareProblem;
});

// HSTS : le navigateur n'utilise plus que HTTPS pendant un an, sous-domaines compris (SS §6).
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

// Les réponses ne disent pas quel serveur web les envoie : l'en-tête « Server: Kestrel » n'aiderait qu'un attaquant (SS §6).
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

var app = builder.Build();

// En tout premier : chaque étape suivante, limites, journal et redirection HTTPS, voit l'adresse et le protocole du client.
app.UseForwardedHeaders();

// Les jours se comptent à l'heure de Paris (voir UsersCalendar) : sans ce fuseau sur la machine, par exemple dans une image
// sans tzdata, l'API refuse de démarrer, au lieu de répondre 500 aux graphiques, aux sacs et aux lectures de la banque.
TimeZoneInfo.FindSystemTimeZoneById(UsersCalendar.TimeZoneId);

// Hors du développement, l'API ne démarre pas sans un vrai moyen d'envoyer les e-mails : personne ne pourrait confirmer son
// adresse ni récupérer son compte. Le contrôle vient après la construction : un test peut ainsi fournir son propre envoi.
if (app.Services.GetRequiredService<IEmailSender>() is EmailSenderNotConfigured)
{
    throw new InvalidOperationException(
        "Aucun envoi réel des e-mails n'est branché : hors du développement, l'API refuse de démarrer sans les réglages « "
        + SmtpConfiguration.SectionName + ":… » du serveur SMTP, au lieu d'écrire les e-mails sur le disque.");
}

// En développement, sans application Enable Banking configurée, le journal technique le dit dès le démarrage.
if (bankProviderSettings == null)
{
    app.Logger.LogWarning(
        "Enable Banking n'est pas configuré : les routes des banques répondent 503. Ajoute « " + BankProviderSettings.ApplicationIdSetting
        + " » et « " + BankProviderSettings.PrivateKeyPathSetting + " » avec dotnet user-secrets.");
}

if (app.Environment.IsDevelopment())
{
    // En développement : la description OpenAPI est publiée,
    // et ASP.NET Core affiche déjà de lui-même le détail des erreurs.
    app.MapOpenApi();
}
else
{
    // En production : des messages d'erreur génériques, et HSTS (SS §6 et §10).
    app.UseExceptionHandler(new ExceptionHandlerOptions
    {
        StatusCodeSelector = ErrorStatusCodeConfiguration.ChooseStatusCode
    });
    app.UseHsts();
}

// Ajoute les en-têtes de sécurité à chaque réponse (SS §6).
app.UseMiddleware<SecurityHeadersMiddleware>();

// Donne le même format aux erreurs sans contenu, comme une adresse inconnue ou le dépassement d'une limite.
app.UseStatusCodePages();

// Redirige toute requête HTTP vers HTTPS (SS §7).
app.UseHttpsRedirection();

// Applique les limites de débit (SS §6), avant toute lecture de la base.
app.UseRateLimiter();

// Reconnaît l'utilisateur à partir de son cookie, puis applique le contrôle d'accès (SS §3 et §4).
app.UseAuthentication();
app.UseAuthorization();

// Relie chaque route à son contrôleur.
app.MapControllers();

// Relie les points de santé, ouverts sans session : /health/live et /health/ready.
app.MapWalletFlowHealthChecks();

app.Run();
