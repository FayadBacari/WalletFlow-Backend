using WalletFlow.Application.BankConnections;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le début d'une autorisation bancaire (GR Comptes, SS §7, DD §5.1), avec un faux prestataire.
public class BankAuthorizationStartServiceTests
{
    private readonly BankServices services = new BankServices();

    // SS §7 : la connexion se lance depuis WalletFlow, avec un state aléatoire et l'adresse fixe du site.
    [Fact]
    public async Task NewConnectionAsksTheBankForOneHundredEightyDays()
    {
        var result = await services.CreateStart().StartNewAsync(services.UserId, FakeBankDataProvider.BankName);

        var started = Assert.IsType<BankAuthorizationStartResult.Started>(result);
        var request = services.Provider.AuthorizationRequests.Single();
        Assert.Equal(FakeBankDataProvider.BankAddress, started.BankAddress);
        Assert.Equal(FakeBankDataProvider.BankName, request.BankName);
        Assert.Equal(services.Now + TimeSpan.FromDays(180) - BankConnectionRules.ClockMargin, request.ValidUntil);
        Assert.Equal(BankServices.WebsiteAddress + "/bank-callback", request.ReturnAddress);
        Assert.True(request.State.Length >= 32);
        Assert.NotNull(services.AuthorizationStore.TryTake(services.TokenGenerator.HashToken(request.State), services.UserId));
    }

    // DD §5.1 : une banque qui n'accepte pas 180 jours reçoit son maximum.
    [Fact]
    public async Task BankWithAShorterMaximumGetsItsMaximum()
    {
        await services.CreateStart().StartNewAsync(services.UserId, FakeBankDataProvider.ShortBankName);

        var request = services.Provider.AuthorizationRequests.Single();
        Assert.Equal(services.Now + TimeSpan.FromDays(90) - BankConnectionRules.ClockMargin, request.ValidUntil);
    }

    // Chaque autorisation a son propre state : deviner celui d'une autre ne marche pas.
    [Fact]
    public async Task EveryAuthorizationHasItsOwnState()
    {
        await services.CreateStart().StartNewAsync(services.UserId, FakeBankDataProvider.BankName);
        await services.CreateStart().StartNewAsync(services.UserId, FakeBankDataProvider.BankName);

        Assert.NotEqual(services.Provider.AuthorizationRequests[0].State, services.Provider.AuthorizationRequests[1].State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Banque inventée")]
    [InlineData("banque de test")]
    public async Task BankThatTheProviderDoesNotOfferIsRefused(string? bankName)
    {
        var result = await services.CreateStart().StartNewAsync(services.UserId, bankName);

        Assert.IsType<BankAuthorizationStartResult.UnknownBank>(result);
        Assert.Empty(services.Provider.AuthorizationRequests);
    }

    [Fact]
    public async Task ProviderDownGivesProviderUnavailable()
    {
        services.Provider.IsDown = true;

        var result = await services.CreateStart().StartNewAsync(services.UserId, FakeBankDataProvider.BankName);

        Assert.IsType<BankAuthorizationStartResult.ProviderUnavailable>(result);
    }

    // GR Comptes : le renouvellement se fait auprès de la même banque, et le state retient la connexion à renouveler.
    [Fact]
    public async Task RenewalUsesTheBankOfTheConnection()
    {
        var connection = await services.ConnectAsync(new TestBankAccount("stable-1", "Compte courant", "EUR", 10000));

        var result = await services.CreateStart().StartRenewalAsync(services.UserId, connection.Id);

        Assert.IsType<BankAuthorizationStartResult.Started>(result);
        var request = services.Provider.AuthorizationRequests.Last();
        var authorization = services.AuthorizationStore.TryTake(services.TokenGenerator.HashToken(request.State), services.UserId);
        Assert.Equal(connection.BankName, request.BankName);
        Assert.Equal(connection.Id, authorization!.RenewedConnectionId);
    }

    // SS §4 : la connexion d'un autre utilisateur n'est pas trouvée.
    [Fact]
    public async Task RenewalOfAnotherUserConnectionIsNotFound()
    {
        var connection = await services.ConnectAsync(new TestBankAccount("stable-1", "Compte courant", "EUR", 10000));

        var result = await services.CreateStart().StartRenewalAsync(Guid.NewGuid(), connection.Id);

        Assert.IsType<BankAuthorizationStartResult.NotFound>(result);
    }
}
