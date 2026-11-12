using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Filters;
using WalletFlow.Api.Middlewares;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.AccountSettings;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Categories;
using WalletFlow.Application.Charts;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.Transactions;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Infrastructure;

// Prépare l'application web de l'API WalletFlow.
var builder = WebApplication.CreateBuilder(args);

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

// L'horloge de l'application : les tests peuvent la remplacer pour vérifier les durées sans attendre.
builder.Services.AddSingleton(TimeProvider.System);

// Chaque réponse de la connexion et de l'inscription dure au moins ce temps : son temps de réponse ne révèle donc pas si un compte utilise l'adresse (SS §2).
// Le réglage est lu au démarrage : une valeur illisible empêche l'API de démarrer.
var minimumDuration = MinimumAnswerDurationFilter.ReadMinimumDuration(builder.Configuration);
builder.Services.AddSingleton(serviceProvider =>
    new MinimumAnswerDurationFilter(minimumDuration, serviceProvider.GetRequiredService<TimeProvider>()));

// Hachage des mots de passe, création des jetons et limites par adresse e-mail (SS §2 et §6).
builder.Services.AddWalletFlowSecurityTools();

// La clé qui chiffre les secrets de double authentification vient des secrets, jamais du code ni de Git (SS §2 et §7).
// Sans elle, aucun compte avec double authentification ne pourrait utiliser son application : l'API refuse donc de démarrer.
var totpEncryptionKey = builder.Configuration["TwoFactorSecretEncryptionKey"];
if (string.IsNullOrWhiteSpace(totpEncryptionKey))
{
    throw new InvalidOperationException(
        "Le réglage « TwoFactorSecretEncryptionKey » est absent. En développement, ajoute-le avec dotnet user-secrets.");
}
builder.Services.AddTotpSecretEncryption(totpEncryptionKey);

// File des e-mails préparés en arrière-plan : le temps de réponse des routes ne révèle aucun compte (SS §2).
builder.Services.AddEmailQueue();

// Texte des e-mails envoyés aux utilisateurs.
builder.Services.AddEmailTexts();

// En développement, les e-mails sont écrits dans un dossier local. L'envoi réel sera branché
// avant la mise en production : d'ici là, l'API refuse de démarrer sans ce réglage.
var emailsFolder = builder.Configuration["LocalEmailsFolder"];
if (string.IsNullOrWhiteSpace(emailsFolder))
{
    throw new InvalidOperationException(
        "Le réglage « LocalEmailsFolder » est absent : aucun moyen d'envoyer les e-mails n'est configuré.");
}
builder.Services.AddLocalFolderEmails(
    Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, emailsFolder)));

// Adresse publique du site, qui sert à construire les liens envoyés par e-mail (SS §9).
builder.Services.AddSingleton(
    new PublicWebsiteSettings(builder.Configuration["PublicWebsiteAddress"]));

// Le prestataire bancaire Enable Banking : l'identifiant de l'application et le chemin de sa clé privée viennent des secrets
// (user-secrets en développement, variables d'environnement en production), jamais du code ni de Git (SS §7).
// En production, l'API refuse de démarrer sans eux. En développement, les routes des banques répondent 503 en attendant.
var bankApplicationId = builder.Configuration["EnableBanking:ApplicationId"];
var bankPrivateKeyPath = builder.Configuration["EnableBanking:PrivateKeyPath"];
var isBankProviderConfigured = !string.IsNullOrWhiteSpace(bankApplicationId) && !string.IsNullOrWhiteSpace(bankPrivateKeyPath);
if (isBankProviderConfigured)
{
    if (!File.Exists(bankPrivateKeyPath))
    {
        throw new InvalidOperationException("La clé privée d'Enable Banking est introuvable au chemin donné par « EnableBanking:PrivateKeyPath ».");
    }

    builder.Services.AddEnableBanking(bankApplicationId!, File.ReadAllText(bankPrivateKeyPath!));
}
else if (builder.Environment.IsDevelopment())
{
    builder.Services.AddBankProviderNotConfigured();
}
else
{
    throw new InvalidOperationException(
        "Les réglages « EnableBanking:ApplicationId » et « EnableBanking:PrivateKeyPath » sont absents : la connexion bancaire n'est pas configurée.");
}
builder.Services.AddBankConnectionTools();

// Outils communs de la couche Application : enregistrement avec réessai, et journal de sécurité.
builder.Services.AddScoped<DatabaseChangesSavingService>();
builder.Services.AddScoped<SecurityJournalService>();

// Cas d'utilisation de la couche Application.
builder.Services.AddScoped<AccountRegistrationService>();
builder.Services.AddScoped<AccountConfirmationService>();
builder.Services.AddScoped<UnconfirmedAccountsCleanupService>();
builder.Services.AddScoped<UserSessionOpeningService>();
builder.Services.AddScoped<AccountLoginService>();
builder.Services.AddScoped<UserSessionService>();
builder.Services.AddScoped<ExpiredUserSessionsCleanupService>();
builder.Services.AddScoped<PasswordRecoveryRequestService>();
builder.Services.AddScoped<BackgroundEmailRequestProcessingService>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<AccountUnlockService>();
builder.Services.AddScoped<TwoFactorCodeCheckingService>();
builder.Services.AddScoped<TwoFactorLoginService>();
builder.Services.AddScoped<TwoFactorActivationService>();
builder.Services.AddScoped<TwoFactorDeactivationService>();
builder.Services.AddScoped<AccountIdentityCheckingService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<PersonalNameChangeService>();
builder.Services.AddScoped<PasswordChangeService>();
builder.Services.AddScoped<EmailChangeRequestService>();
builder.Services.AddScoped<EmailChangeConfirmationService>();
builder.Services.AddScoped<CategoryListService>();
builder.Services.AddScoped<CategoryCreationService>();
builder.Services.AddScoped<CategoryRenameService>();
builder.Services.AddScoped<CategoryBudgetService>();
builder.Services.AddScoped<CategoryDeletionService>();
builder.Services.AddScoped<BankListService>();
builder.Services.AddScoped<BankAuthorizationStartService>();
builder.Services.AddScoped<BankConnectionFinishService>();
builder.Services.AddScoped<BankConnectionListService>();
builder.Services.AddScoped<BankBalanceUpdateService>();
builder.Services.AddScoped<BankBackgroundUpdateService>();
builder.Services.AddScoped<BankDisconnectionService>();
builder.Services.AddScoped<BankBalanceTotalService>();
builder.Services.AddScoped<BankTransactionArrival>();
builder.Services.AddScoped<TransactionSearchService>();
builder.Services.AddScoped<TransactionCategoryChangeService>();
builder.Services.AddScoped<TransactionTransferService>();
builder.Services.AddScoped<CategoryExpenseChartService>();
builder.Services.AddScoped<MonthlyTotalsChartService>();
builder.Services.AddScoped<MonthComparisonService>();

// Tâches de fond : effacent chaque jour les comptes non confirmés dont le lien a expiré (GR Auth) et les sessions expirées,
// préparent les e-mails de récupération et de déblocage, et mettent à jour les soldes jusqu'à 4 fois par jour (GR Comptes).
builder.Services.AddHostedService<UnconfirmedAccountsCleanupBackgroundService>();
builder.Services.AddHostedService<ExpiredUserSessionsCleanupBackgroundService>();
builder.Services.AddHostedService<BackgroundEmailRequestsBackgroundService>();
builder.Services.AddHostedService<BankBalancesBackgroundService>();

// Limites de débit des points d'entrée sensibles (SS §6).
builder.Services.AddWalletFlowRateLimiting();

// L'utilisateur est reconnu par son cookie de session, gardée côté serveur (SS §3).
builder.Services
    .AddAuthentication(UserSessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, UserSessionAuthenticationHandler>(UserSessionAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();

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

// Décrit l'API au format OpenAPI (publié seulement en développement).
builder.Services.AddOpenApi();

// Les erreurs sont renvoyées au format « problem details », en français et sans détail technique (SS §10).
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ApiErrorConfiguration.TranslateTitle;
});

// HSTS : le navigateur n'utilise plus que HTTPS pendant un an, sous-domaines compris (SS §6).
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

var app = builder.Build();

// Les graphiques comptent le mois en cours à l'heure de Paris (voir ChartRules) : sans ce fuseau sur la machine, par exemple
// dans une image sans tzdata, l'API refuse de démarrer, au lieu de répondre 500 à chaque comparaison.
TimeZoneInfo.FindSystemTimeZoneById(ChartRules.UsersTimeZoneId);

// En développement, sans application Enable Banking configurée, le journal technique le dit dès le démarrage.
if (!isBankProviderConfigured)
{
    app.Logger.LogWarning(
        "Enable Banking n'est pas configuré : les routes des banques répondent 503. Ajoute « EnableBanking:ApplicationId » et « EnableBanking:PrivateKeyPath » avec dotnet user-secrets.");
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

app.Run();
