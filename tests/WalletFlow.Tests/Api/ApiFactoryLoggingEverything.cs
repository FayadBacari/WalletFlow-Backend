using Microsoft.AspNetCore.Hosting;

namespace WalletFlow.Tests.Api;

// Lance l'API avec le journal technique au niveau le plus détaillé, pour vérifier ce qu'il pourrait contenir.
public class ApiFactoryLoggingEverything : ApiFactoryWithFakeRepositoryAndEmails
{
    // Monte le niveau du journal avant le démarrage de l'API (nom imposé par le framework).
    protected override void ConfigureWebHost(IWebHostBuilder builderOfTheApiStartedForTheTests)
    {
        base.ConfigureWebHost(builderOfTheApiStartedForTheTests);

        builderOfTheApiStartedForTheTests.UseSetting("Logging:LogLevel:Default", "Trace");
        builderOfTheApiStartedForTheTests.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Trace");
    }
}
