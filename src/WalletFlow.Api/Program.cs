using WalletFlow.Api.Middlewares;
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

// Les contrôleurs reçoivent les requêtes HTTP et appellent la couche Application.
builderOfTheWalletFlowWebApplication.Services.AddControllers();

// Décrit l'API au format OpenAPI (publié seulement en développement).
builderOfTheWalletFlowWebApplication.Services.AddOpenApi();

// Les erreurs sont renvoyées au format « problem details », sans détail technique (SS §10).
builderOfTheWalletFlowWebApplication.Services.AddProblemDetails();

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
    walletFlowWebApplicationReadyToHandleRequests.UseExceptionHandler();
    walletFlowWebApplicationReadyToHandleRequests.UseHsts();
}

// Ajoute les en-têtes de sécurité à chaque réponse (SS §6).
walletFlowWebApplicationReadyToHandleRequests.UseMiddleware<SecurityHeadersMiddleware>();

// Redirige toute requête HTTP vers HTTPS (SS §7).
walletFlowWebApplicationReadyToHandleRequests.UseHttpsRedirection();

// Relie chaque route à son contrôleur.
walletFlowWebApplicationReadyToHandleRequests.MapControllers();

walletFlowWebApplicationReadyToHandleRequests.Run();
