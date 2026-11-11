using Microsoft.AspNetCore.Hosting;
using WalletFlow.Api.Filters;

namespace WalletFlow.Tests.Api;

// Lance l'API avec une durée minimale pour chaque réponse de la connexion et de l'inscription, comme en production.
public class ApiFactoryWithMinimumAnswerDuration : ApiFactoryWithFakes
{
    public const int MinimumMilliseconds = 300;

    // Remet la durée minimale que les autres tests de l'API enlèvent (nom imposé par le framework).
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting(
            MinimumAnswerDurationFilter.SettingName,
            MinimumMilliseconds.ToString());
    }
}
