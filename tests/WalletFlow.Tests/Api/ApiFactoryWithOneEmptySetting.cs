using Microsoft.AspNetCore.Hosting;

namespace WalletFlow.Tests.Api;

// Lance l'API en mode production en laissant vide un réglage obligatoire, pour vérifier qu'elle refuse de démarrer.
public class ApiFactoryWithOneEmptySetting : ApiFactoryRunningLikeInProduction
{
    private readonly string nameOfTheSettingLeftEmptyForThisTest;

    // Reçoit le nom du réglage à laisser vide.
    public ApiFactoryWithOneEmptySetting(string nameOfTheSettingLeftEmptyForThisTest)
    {
        this.nameOfTheSettingLeftEmptyForThisTest = nameOfTheSettingLeftEmptyForThisTest;
    }

    // Vide le réglage avant le démarrage de l'API (nom imposé par le framework).
    protected override void ConfigureWebHost(IWebHostBuilder builderOfTheApiStartedForTheTests)
    {
        base.ConfigureWebHost(builderOfTheApiStartedForTheTests);

        builderOfTheApiStartedForTheTests.UseSetting(nameOfTheSettingLeftEmptyForThisTest, "");
    }
}
