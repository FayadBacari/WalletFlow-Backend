using Microsoft.AspNetCore.Hosting;

namespace WalletFlow.Tests.Api;

// Lance l'API en mode production, pour tester ce qu'un vrai visiteur reçoit : erreurs génériques et HSTS.
public class ApiFactoryRunningLikeInProduction : ApiFactoryWithFakeRepositoryAndEmails
{
    // Change le mode de l'API avant son démarrage (nom imposé par le framework).
    protected override void ConfigureWebHost(IWebHostBuilder builderOfTheApiStartedForTheTests)
    {
        base.ConfigureWebHost(builderOfTheApiStartedForTheTests);

        builderOfTheApiStartedForTheTests.UseEnvironment("Production");

        // Ces deux réglages viennent d'appsettings.Development.json, qui n'est pas lu en production.
        // Le dossier n'est jamais utilisé : les e-mails restent en mémoire dans les tests.
        builderOfTheApiStartedForTheTests.UseSetting("LocalEmailsFolder", "dossier-jamais-utilise");
        builderOfTheApiStartedForTheTests.UseSetting("PublicWebsiteAddress", "https://walletflow.test");
    }
}
