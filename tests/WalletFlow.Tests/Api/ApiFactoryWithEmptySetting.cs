using Microsoft.AspNetCore.Hosting;

namespace WalletFlow.Tests.Api;

// Lance l'API en mode production en laissant vide un réglage obligatoire, pour vérifier qu'elle refuse de démarrer.
public class ApiFactoryWithEmptySetting : ApiFactoryInProduction
{
    private readonly string emptySettingName;

    // Reçoit le nom du réglage à laisser vide.
    public ApiFactoryWithEmptySetting(string emptySettingName)
    {
        this.emptySettingName = emptySettingName;
    }

    // Vide le réglage avant le démarrage de l'API (nom imposé par le framework).
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting(emptySettingName, "");
    }
}
