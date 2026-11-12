using WalletFlow.Application.BankConnections;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie la liste des banques et celle des connexions de l'utilisateur (GR Comptes, SS §4).
public class BankListServicesTests
{
    private readonly BankServices services = new BankServices();

    [Fact]
    public async Task BanksAreListedInTheFrenchOrder()
    {
        services.Provider.Banks.Add(new ProvidedBank("Crédit Agricole", TimeSpan.FromDays(180)));
        services.Provider.Banks.Add(new ProvidedBank("Boursorama", TimeSpan.FromDays(180)));

        var result = await new BankListService(services.Provider).ListAsync();

        var listed = Assert.IsType<BankListResult.Listed>(result);
        Assert.Equal(
            new List<string> { "Banque à 90 jours", "Banque de test", "Boursorama", "Crédit Agricole" },
            listed.Banks.Select(bank => bank.Name).ToList());
    }

    [Fact]
    public async Task ProviderDownGivesProviderUnavailable()
    {
        services.Provider.IsDown = true;

        var result = await new BankListService(services.Provider).ListAsync();

        Assert.IsType<BankListResult.ProviderUnavailable>(result);
    }

    // GR Comptes : chaque connexion avec ses comptes ; les connexions des autres n'y sont jamais (SS §4).
    [Fact]
    public async Task UserSeesOnlyHisConnectionsWithTheirAccounts()
    {
        var connection = await services.ConnectAsync(
            new TestBankAccount("stable-2", "Livret A", "EUR", 300),
            new TestBankAccount("stable-1", "Compte courant", "EUR", 100));
        services.Repository.SavedConnections.Add(new BankConnection
        {
            ExternalConnectionId = "session-d-un-autre",
            BankName = "Banque d'un autre",
            AuthorizationExpiresAt = services.Now.AddDays(100),
            UserId = Guid.NewGuid()
        });

        var views = await services.CreateConnectionList().ListAsync(services.UserId);

        var view = Assert.Single(views);
        Assert.Equal(connection.Id, view.Connection.Id);
        Assert.Equal(new List<string> { "Compte courant", "Livret A" }, view.Accounts.Select(account => account.Name).ToList());
        Assert.Equal(BankConnectionStatus.Active, view.Status);
        Assert.False(view.IsEndingSoon);
    }

    // GR Comptes : l'utilisateur est prévenu 7 jours avant la fin, et voit une connexion expirée dès que sa date est passée.
    [Fact]
    public async Task ListWarnsBeforeTheEndAndShowsTheExpiry()
    {
        await services.ConnectAsync(new TestBankAccount("stable-1", "Compte courant", "EUR", 100));

        services.Clock.Advance(TimeSpan.FromDays(173));
        var endingSoon = Assert.Single(await services.CreateConnectionList().ListAsync(services.UserId));

        services.Clock.Advance(TimeSpan.FromDays(7));
        var ended = Assert.Single(await services.CreateConnectionList().ListAsync(services.UserId));

        Assert.True(endingSoon.IsEndingSoon);
        Assert.Equal(BankConnectionStatus.Active, endingSoon.Status);
        Assert.False(ended.IsEndingSoon);
        Assert.Equal(BankConnectionStatus.Expired, ended.Status);
    }
}
