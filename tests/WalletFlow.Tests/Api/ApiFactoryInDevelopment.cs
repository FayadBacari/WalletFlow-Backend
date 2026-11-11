using Microsoft.AspNetCore.Hosting;

namespace WalletFlow.Tests.Api;

// Lance l'API en mode développement, pour tester ce qui n'existe qu'en développement, comme la description OpenAPI.
// Ce mode lit aussi les user-secrets de la machine : les réglages donnés par les tests passent quand même avant eux.
public class ApiFactoryInDevelopment : ApiFactoryWithFakes
{
    // Change le mode de l'API avant son démarrage (nom imposé par le framework).
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseEnvironment("Development");
    }
}
