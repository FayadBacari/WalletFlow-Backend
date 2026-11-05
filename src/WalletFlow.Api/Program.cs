using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Middlewares;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Infrastructure;

// Prépare l'application web de l'API WalletFlow.
var builderOfTheWalletFlowWebApplication = WebApplication.CreateBuilder(args);

// La chaîne de connexion vient des secrets (user-secrets en développement, variables
// d'environnement en production), jamais du code ni de Git (SS §7).
var connectionStringOfTheWalletFlowDatabase =
    builderOfTheWalletFlowWebApplication.Configuration.GetConnectionString("WalletFlowDatabase");
if (string.IsNullOrWhiteSpace(connectionStringOfTheWalletFlowDatabase))
{
    throw new InvalidOperationException(
        "La chaîne de connexion « WalletFlowDatabase » est absente. En développement, ajoute-la avec dotnet user-secrets.");
}
builderOfTheWalletFlowWebApplication.Services.AddWalletFlowDatabaseAccessToTheServices(connectionStringOfTheWalletFlowDatabase);

// L'horloge de l'application : les tests peuvent la remplacer pour vérifier les durées sans attendre.
builderOfTheWalletFlowWebApplication.Services.AddSingleton(TimeProvider.System);

// Hachage des mots de passe, création des jetons et limites par adresse e-mail (SS §2 et §6).
builderOfTheWalletFlowWebApplication.Services.AddWalletFlowSecurityToolsToTheServices();

// File des e-mails préparés en arrière-plan : le temps de réponse des routes ne révèle aucun compte (SS §2).
builderOfTheWalletFlowWebApplication.Services.AddBackgroundEmailRequestQueueToTheServices();

// En développement, les e-mails sont écrits dans un dossier local. L'envoi réel sera branché
// avant la mise en production : d'ici là, l'API refuse de démarrer sans ce réglage.
var localEmailsFolderFromTheConfiguration = builderOfTheWalletFlowWebApplication.Configuration["LocalEmailsFolder"];
if (string.IsNullOrWhiteSpace(localEmailsFolderFromTheConfiguration))
{
    throw new InvalidOperationException(
        "Le réglage « LocalEmailsFolder » est absent : aucun moyen d'envoyer les e-mails n'est configuré.");
}
builderOfTheWalletFlowWebApplication.Services.AddEmailsWrittenToLocalFolderToTheServices(
    Path.GetFullPath(Path.Combine(builderOfTheWalletFlowWebApplication.Environment.ContentRootPath, localEmailsFolderFromTheConfiguration)));

// Adresse publique du site, qui sert à construire les liens envoyés par e-mail (SS §9).
builderOfTheWalletFlowWebApplication.Services.AddSingleton(
    new PublicWebsiteSettings(builderOfTheWalletFlowWebApplication.Configuration["PublicWebsiteAddress"]));

// Cas d'utilisation de la couche Application.
builderOfTheWalletFlowWebApplication.Services.AddScoped<AccountRegistrationService>();
builderOfTheWalletFlowWebApplication.Services.AddScoped<AccountConfirmationService>();
builderOfTheWalletFlowWebApplication.Services.AddScoped<UnconfirmedAccountsCleanupService>();
builderOfTheWalletFlowWebApplication.Services.AddScoped<AccountLoginService>();
builderOfTheWalletFlowWebApplication.Services.AddScoped<UserSessionService>();
builderOfTheWalletFlowWebApplication.Services.AddScoped<ExpiredUserSessionsCleanupService>();
builderOfTheWalletFlowWebApplication.Services.AddScoped<PasswordRecoveryRequestService>();
builderOfTheWalletFlowWebApplication.Services.AddScoped<BackgroundEmailRequestProcessingService>();
builderOfTheWalletFlowWebApplication.Services.AddScoped<PasswordResetService>();
builderOfTheWalletFlowWebApplication.Services.AddScoped<AccountUnlockService>();

// Tâches de fond : effacent chaque jour les comptes non confirmés dont le lien a expiré (GR Auth) et les sessions expirées,
// et préparent les e-mails de récupération et de déblocage.
builderOfTheWalletFlowWebApplication.Services.AddHostedService<UnconfirmedAccountsCleanupBackgroundService>();
builderOfTheWalletFlowWebApplication.Services.AddHostedService<ExpiredUserSessionsCleanupBackgroundService>();
builderOfTheWalletFlowWebApplication.Services.AddHostedService<BackgroundEmailRequestsBackgroundService>();

// Limites de débit des points d'entrée sensibles (SS §6).
builderOfTheWalletFlowWebApplication.Services.AddWalletFlowRateLimitingToTheServices();

// L'utilisateur est reconnu par son cookie de session, gardée côté serveur (SS §3).
builderOfTheWalletFlowWebApplication.Services
    .AddAuthentication(UserSessionAuthenticationHandler.NameOfTheUserSessionAuthenticationScheme)
    .AddScheme<AuthenticationSchemeOptions, UserSessionAuthenticationHandler>(UserSessionAuthenticationHandler.NameOfTheUserSessionAuthenticationScheme, null);
builderOfTheWalletFlowWebApplication.Services.AddAuthorization();

// Jeton anti-CSRF fourni par ASP.NET Core (SS §6).
builderOfTheWalletFlowWebApplication.Services.AddAntiforgery(AntiforgeryConfiguration.ApplyOptionsOfTheCsrfToken);

// Les contrôleurs reçoivent les requêtes HTTP et appellent la couche Application.
// Une requête refusée reçoit toujours le même format d'erreur, sans aucun nom interne du code (SS §10).
// AddControllersWithViews plutôt que AddControllers : le filtre anti-CSRF d'ASP.NET Core en fait partie. L'API n'a aucune vue.
builderOfTheWalletFlowWebApplication.Services
    .AddControllersWithViews(optionsOfTheControllersOfTheApi =>
    {
        // Toute route demande une session et le jeton anti-CSRF, même si on oublie de le demander :
        // une route publique doit le dire avec [AllowAnonymous] et [IgnoreAntiforgeryToken] (SS §4 et §6).
        optionsOfTheControllersOfTheApi.Filters.Add(new AuthorizeFilter());
        optionsOfTheControllersOfTheApi.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    })
    .ConfigureApiBehaviorOptions(optionsOfTheAutomaticControllerBehavior =>
    {
        optionsOfTheAutomaticControllerBehavior.InvalidModelStateResponseFactory = ApiErrorConfiguration.CreateAnswerForTheRefusedRequest;
    })
    .AddJsonOptions(optionsOfTheJsonRequestBodiesFromVisitors =>
    {
        optionsOfTheJsonRequestBodiesFromVisitors.AllowInputFormatterExceptionMessages = false;
    });

// Décrit l'API au format OpenAPI (publié seulement en développement).
builderOfTheWalletFlowWebApplication.Services.AddOpenApi();

// Les erreurs sont renvoyées au format « problem details », en français et sans détail technique (SS §10).
builderOfTheWalletFlowWebApplication.Services.AddProblemDetails(optionsOfTheProblemDetailsAnswers =>
{
    optionsOfTheProblemDetailsAnswers.CustomizeProblemDetails = ApiErrorConfiguration.TranslateTheTitleOfTheProblemIntoFrench;
});

// HSTS : le navigateur n'utilise plus que HTTPS pendant un an, sous-domaines compris (SS §6).
builderOfTheWalletFlowWebApplication.Services.AddHsts(optionsOfTheStrictTransportSecurityHeader =>
{
    optionsOfTheStrictTransportSecurityHeader.MaxAge = TimeSpan.FromDays(365);
    optionsOfTheStrictTransportSecurityHeader.IncludeSubDomains = true;
});

var walletFlowWebApplicationReadyToHandleRequests = builderOfTheWalletFlowWebApplication.Build();

if (walletFlowWebApplicationReadyToHandleRequests.Environment.IsDevelopment())
{
    // En développement : la description OpenAPI est publiée,
    // et ASP.NET Core affiche déjà de lui-même le détail des erreurs.
    walletFlowWebApplicationReadyToHandleRequests.MapOpenApi();
}
else
{
    // En production : des messages d'erreur génériques, et HSTS (SS §6 et §10).
    walletFlowWebApplicationReadyToHandleRequests.UseExceptionHandler(new ExceptionHandlerOptions
    {
        StatusCodeSelector = ErrorStatusCodeConfiguration.ChooseStatusCodeForTheCaughtException
    });
    walletFlowWebApplicationReadyToHandleRequests.UseHsts();
}

// Ajoute les en-têtes de sécurité à chaque réponse (SS §6).
walletFlowWebApplicationReadyToHandleRequests.UseMiddleware<SecurityHeadersMiddleware>();

// Donne le même format aux erreurs sans contenu, comme une adresse inconnue ou le dépassement d'une limite.
walletFlowWebApplicationReadyToHandleRequests.UseStatusCodePages();

// Redirige toute requête HTTP vers HTTPS (SS §7).
walletFlowWebApplicationReadyToHandleRequests.UseHttpsRedirection();

// Applique les limites de débit (SS §6), avant toute lecture de la base.
walletFlowWebApplicationReadyToHandleRequests.UseRateLimiter();

// Reconnaît l'utilisateur à partir de son cookie, puis applique le contrôle d'accès (SS §3 et §4).
walletFlowWebApplicationReadyToHandleRequests.UseAuthentication();
walletFlowWebApplicationReadyToHandleRequests.UseAuthorization();

// Relie chaque route à son contrôleur.
walletFlowWebApplicationReadyToHandleRequests.MapControllers();

walletFlowWebApplicationReadyToHandleRequests.Run();
