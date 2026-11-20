using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Application.Security;
using WalletFlow.Infrastructure;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.EndToEnd;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Api;

// Vérifie que les tests de l'API ne dépendent pas de la machine qui les lance.
public class TestApiSettingsTests
{
    // Ni les user-secrets ni appsettings.Development.json ne sont lus, et aucune clé n'est lue ou écrite dans le dossier personnel :
    // les clés vont dans le dossier temporaire de ce lancement des tests, par le code de production.
    [Fact]
    public void ApiOfTheTestsIgnoresTheMachineSettingsAndKeepsItsKeysInATemporaryFolder()
    {
        using var api = new ApiFactoryWithFakes();

        var environment = api.Services.GetRequiredService<IWebHostEnvironment>();
        var keyRepository = api.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository;

        Assert.Equal(TestApiSettings.EnvironmentName, environment.EnvironmentName);
        Assert.Equal(TestApiSettings.KeysFolder, Assert.IsType<FileSystemXmlRepository>(keyRepository).Directory.FullName);
        Assert.StartsWith(Path.GetTempPath(), TestApiSettings.KeysFolder, StringComparison.Ordinal);
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

    // L'API branche le vrai Pwned Passwords ; les API des tests le remplacent toutes : aucun test ne sort sur le réseau (SS §2).
    [Fact]
    public void RealApiSearchesPwnedPasswordsAndTestApisNeverDo()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWalletFlowSecurityTools(Environment.ProcessorCount);
        using var serviceProvider = services.BuildServiceProvider();
        using var fakeDatabaseApi = new ApiFactoryWithFakes();
        using var realDatabaseApi = new ApiFactoryWithRealDatabase();

        Assert.IsType<PwnedPasswordsChecker>(serviceProvider.GetRequiredService<IBreachedPasswordChecker>());
        Assert.IsType<FakeBreachedPasswordChecker>(fakeDatabaseApi.Services.GetRequiredService<IBreachedPasswordChecker>());
        Assert.IsType<FakeBreachedPasswordChecker>(realDatabaseApi.Services.GetRequiredService<IBreachedPasswordChecker>());
    }
}
