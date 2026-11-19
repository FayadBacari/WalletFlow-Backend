using System.Net;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Api.Configuration;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Tests.Infrastructure;

namespace WalletFlow.Tests.EndToEnd;

// Vérifie le point de santé « ready » sur la vraie base (SS §11) : il passe par la vraie connexion d'EF Core, et une base
// injoignable est bien vue comme telle, sans faire tomber l'API.
[Collection(RealDatabaseCollection.Name)]
public class HealthJourneyTests
{
    // La vraie API, branchée sur la base de test, se dit prête.
    [Fact]
    public async Task RealApiOnTheTestDatabaseIsReady()
    {
        using var api = new ApiFactoryWithRealDatabase();

        var response = await api.CreateClient().GetAsync(HealthConfiguration.ReadyPath);

        Assert.Equal((HttpStatusCode.OK, "Healthy"), (response.StatusCode, await response.Content.ReadAsStringAsync()));
    }

    // Une base où personne n'écoute est vue comme injoignable : la vérification répond faux, sans lever d'erreur.
    [Fact]
    public async Task UnreachableDatabaseIsSeenAsDown()
    {
        var options = new DbContextOptionsBuilder<WalletFlowDatabaseContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=walletflow;Username=walletflow_app;Timeout=2")
            .Options;
        await using var unreachableDatabase = new WalletFlowDatabaseContext(options);
        await using var testDatabase = LocalTestDatabase.CreateDatabaseContext();

        Assert.False(await new DatabaseHealth(unreachableDatabase).CanConnectAsync());
        Assert.True(await new DatabaseHealth(testDatabase).CanConnectAsync());
    }
}
