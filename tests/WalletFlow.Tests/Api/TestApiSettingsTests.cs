using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace WalletFlow.Tests.Api;

// Vérifie que les tests de l'API ne dépendent pas de la machine qui les lance.
public class TestApiSettingsTests
{
    // Ni les user-secrets ni appsettings.Development.json ne sont lus, et aucune clé n'est lue ou écrite dans le dossier personnel.
    [Fact]
    public void ApiOfTheTestsIgnoresTheMachineSettingsAndKeepsItsKeysInMemory()
    {
        using var api = new ApiFactoryWithFakes();

        var environment = api.Services.GetRequiredService<IWebHostEnvironment>();
        var dataProtectionProvider = api.Services.GetRequiredService<IDataProtectionProvider>();

        Assert.Equal(TestApiSettings.EnvironmentName, environment.EnvironmentName);
        Assert.IsType<EphemeralDataProtectionProvider>(dataProtectionProvider);
    }
}
