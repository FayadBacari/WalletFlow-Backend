using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Api.Configuration;
using WalletFlow.Infrastructure.Security;

namespace WalletFlow.Tests.Api;

// Vérifie le réglage du nombre de calculs Argon2id faits en même temps : le nombre de cœurs sans réglage, la valeur réglée
// sinon, et un refus de démarrer pour une valeur illisible ou hors des bornes.
public class ArgonConfigurationTests
{
    // Crée une configuration qui ne contient que ce réglage, ou rien.
    private static IConfiguration CreateConfiguration(string? settingValue)
    {
        var settings = new Dictionary<string, string?> { [ArgonConfiguration.ConcurrentComputationsSetting] = settingValue };

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    // Sans réglage, autant de calculs à la fois que de cœurs.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingSettingGivesTheProcessorCount(string? settingValue)
    {
        Assert.Equal(Environment.ProcessorCount, ArgonConfiguration.ReadConcurrentComputations(CreateConfiguration(settingValue)));
    }

    // Une valeur entre 1 et 64 est lue telle quelle.
    [Theory]
    [InlineData("1", 1)]
    [InlineData("3", 3)]
    [InlineData("64", 64)]
    public void ValidSettingIsRead(string settingValue, int expectedCount)
    {
        Assert.Equal(expectedCount, ArgonConfiguration.ReadConcurrentComputations(CreateConfiguration(settingValue)));
    }

    // Une valeur illisible, nulle, négative ou au-delà de 64 est refusée, avec le nom du réglage et ses bornes.
    [Theory]
    [InlineData("0")]
    [InlineData("65")]
    [InlineData("-1")]
    [InlineData("+4")]
    [InlineData("4.5")]
    [InlineData(" 4")]
    [InlineData("quatre")]
    [InlineData("99999999999")]
    public void UnreadableOrOutOfBoundsSettingIsRefused(string settingValue)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ArgonConfiguration.ReadConcurrentComputations(CreateConfiguration(settingValue)));

        Assert.Contains(ArgonConfiguration.ConcurrentComputationsSetting, exception.Message);
        Assert.Contains("de 1 à 64", exception.Message);
    }

    // L'API crée son limiteur avec la valeur réglée, et les API des tests avec la leur, la même sur toutes les machines.
    [Fact]
    public void ApiUsesTheSetting()
    {
        using var testApi = new ApiFactoryInProduction();
        using var productionApi = new ApiFactoryInProduction();
        using var apiWithThree = productionApi.WithWebHostBuilder(apiBuilder =>
            apiBuilder.UseSetting(ArgonConfiguration.ConcurrentComputationsSetting, "3"));

        Assert.Equal(
            TestApiSettings.ArgonConcurrentComputations,
            testApi.Services.GetRequiredService<ArgonComputationsLimiter>().ConcurrentComputations);
        Assert.Equal(3, apiWithThree.Services.GetRequiredService<ArgonComputationsLimiter>().ConcurrentComputations);
    }

    // Avec une valeur fausse, l'API refuse de démarrer et dit quel réglage corriger.
    [Fact]
    public void ApiRefusesToStartWithAnUnreadableSetting()
    {
        using var productionApi = new ApiFactoryInProduction();
        using var misconfiguredApi = productionApi.WithWebHostBuilder(apiBuilder =>
            apiBuilder.UseSetting(ArgonConfiguration.ConcurrentComputationsSetting, "0"));

        var exception = Assert.Throws<InvalidOperationException>(() => misconfiguredApi.CreateClient());

        Assert.Contains(ArgonConfiguration.ConcurrentComputationsSetting, exception.Message);
    }
}
