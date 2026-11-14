using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
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

    // Aucun fichier de réglages n'est surveillé : sous Linux, chaque surveillance compte dans une limite de 128,
    // et les tests lancent bien plus d'API que cela (CI de l'étape 10).
    [Fact]
    public void ApiOfTheTestsWatchesNoSettingsFile()
    {
        using var api = new ApiFactoryWithFakes();

        var configuration = (IConfigurationRoot)api.Services.GetRequiredService<IConfiguration>();
        var settingsFiles = configuration.Providers.OfType<FileConfigurationProvider>().ToList();

        Assert.NotEmpty(settingsFiles);
        Assert.All(settingsFiles, settingsFile => Assert.False(settingsFile.Source.ReloadOnChange));
    }
}
