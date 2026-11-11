using Microsoft.AspNetCore.Hosting;

namespace WalletFlow.Tests.Api;

// Lance l'API en mode production, pour tester ce qu'un vrai visiteur reçoit : erreurs génériques et HSTS.
public class ApiFactoryInProduction : ApiFactoryWithFakes
{
    // Change le mode de l'API avant son démarrage (nom imposé par le framework).
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseEnvironment("Production");
    }
}
