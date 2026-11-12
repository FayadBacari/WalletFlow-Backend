using WalletFlow.Application.BankConnections;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le retour de la banque : connexion, renouvellement, et chaque refus (GR Comptes, SS §7 et §11, DD §5).
public class BankConnectionFinishServiceTests
{
    private readonly BankServices services = new BankServices();

    // Termine l'autorisation de ce state avec ce code, pour l'utilisateur des tests.
    private async Task<BankConnectionFinishResult> FinishAsync(string code, string state, Guid? userId = null)
    {
        return await services.CreateFinish().FinishAsync(userId ?? services.UserId, code, state, services.Presence);
    }

    // Donne les événements du journal de ce type et de ce résultat.
    private int CountEvents(SecurityEventType type, SecurityEventResult result)
    {
        return services.Journal.SavedSecurityEvents.Count(saved => saved.Type == type && saved.Result == result);
    }

    // GR Comptes : la connexion garde les comptes en euros, avec leur solde comptable, et la date de fin de l'autorisation.
    [Fact]
    public async Task ConnectionKeepsTheEuroAccountsWithTheirBookedBalance()
    {
        var state = await services.StartAsync();
        var session = services.Provider.AddSession("code-1",
            new TestBankAccount("stable-1", "  Compte courant ", "EUR", 152_034),
            new TestBankAccount("stable-2", "Livret A", "EUR", -500),
            new TestBankAccount("stable-3", "Compte en dollars", "USD", 9000));

        var result = await FinishAsync("code-1", state);

        var connected = Assert.IsType<BankConnectionFinishResult.Connected>(result);
        var connection = services.Repository.SavedConnections.Single();
        var accounts = services.AccountsOf(connection).OrderBy(account => account.Name).ToList();
        Assert.Equal(2, connected.AddedAccountCount);
        Assert.Equal(new List<string> { "Compte en dollars" }, connected.SkippedAccountNames);
        Assert.Equal(session.SessionId, connection.ExternalConnectionId);
        Assert.Equal(FakeBankDataProvider.BankName, connection.BankName);
        Assert.Equal(BankConnectionStatus.Active, connection.Status);
        Assert.Equal(session.ValidUntil, connection.AuthorizationExpiresAt);
        Assert.Equal(services.UserId, connection.UserId);
        Assert.Equal("Compte courant", accounts[0].Name);
        Assert.Equal(152_034, accounts[0].BalanceInCents);
        Assert.Equal("stable-1", accounts[0].ExternalAccountId);
        Assert.Equal(services.Now, accounts[0].BalanceUpdatedAt);
        Assert.Equal(-500, accounts[1].BalanceInCents);
        Assert.Equal(1, CountEvents(SecurityEventType.BankConnection, SecurityEventResult.Success));
    }

    // DSP2 : l'utilisateur est là au retour de la banque : sa présence part avec chaque lecture de solde.
    [Fact]
    public async Task BalancesAreReadWithTheUserPresence()
    {
        var state = await services.StartAsync();
        services.Provider.AddSession("code-1", new TestBankAccount("stable-1", "Compte courant", "EUR", 100));

        await FinishAsync("code-1", state);

        Assert.Equal(services.Presence, Assert.Single(services.Provider.BalanceReadPresences));
    }

    // SS §7 : le state d'un autre compte est refusé, son code n'est même pas utilisé, et le state ne sert plus à son auteur.
    [Fact]
    public async Task StateOfAnotherUserIsRefusedWithoutUsingTheCode()
    {
        var attackerState = await services.StartAsync();
        services.Provider.AddSession("code-de-la-victime", new TestBankAccount("stable-1", "Compte courant", "EUR", 100));
        var victimId = Guid.NewGuid();

        var victimResult = await FinishAsync("code-de-la-victime", attackerState, victimId);
        var attackerResult = await FinishAsync("code-de-la-victime", attackerState);

        Assert.IsType<BankConnectionFinishResult.LinkNoLongerValid>(victimResult);
        Assert.IsType<BankConnectionFinishResult.LinkNoLongerValid>(attackerResult);
        Assert.Equal(0, services.Provider.UsedCodeCount);
        Assert.Empty(services.Repository.SavedConnections);
        Assert.Equal(2, CountEvents(SecurityEventType.BankConnection, SecurityEventResult.Failure));
    }

    [Theory]
    [InlineData("state-inconnu", "code-1")]
    [InlineData("", "code-1")]
    [InlineData(null, "code-1")]
    public async Task UnknownOrMissingStateIsRefused(string? state, string code)
    {
        services.Provider.AddSession("code-1", new TestBankAccount("stable-1", "Compte courant", "EUR", 100));

        var result = await services.CreateFinish().FinishAsync(services.UserId, code, state, services.Presence);

        Assert.IsType<BankConnectionFinishResult.LinkNoLongerValid>(result);
        Assert.Equal(0, services.Provider.UsedCodeCount);
    }

    // SS §7 : un state ne sert qu'une fois.
    [Fact]
    public async Task StateServesOnlyOnce()
    {
        var state = await services.StartAsync();
        services.Provider.AddSession("code-1", new TestBankAccount("stable-1", "Compte courant", "EUR", 100));
        services.Provider.AddSession("code-2", new TestBankAccount("stable-2", "Livret A", "EUR", 100));

        await FinishAsync("code-1", state);
        var second = await FinishAsync("code-2", state);

        Assert.IsType<BankConnectionFinishResult.LinkNoLongerValid>(second);
        Assert.Single(services.Repository.SavedConnections);
    }

    [Fact]
    public async Task StateExpiresAfterFifteenMinutes()
    {
        var state = await services.StartAsync();
        services.Provider.AddSession("code-1", new TestBankAccount("stable-1", "Compte courant", "EUR", 100));
        services.Clock.Advance(TimeSpan.FromMinutes(15));

        var result = await FinishAsync("code-1", state);

        Assert.IsType<BankConnectionFinishResult.LinkNoLongerValid>(result);
    }

    [Fact]
    public async Task CodeRefusedByTheBankIsJournaled()
    {
        var state = await services.StartAsync();

        var result = await FinishAsync("code-faux", state);

        Assert.IsType<BankConnectionFinishResult.RefusedByBank>(result);
        Assert.Equal(1, CountEvents(SecurityEventType.BankConnection, SecurityEventResult.Failure));
        Assert.Empty(services.Repository.SavedConnections);
    }

    [Fact]
    public async Task ProviderDownDuringTheSessionCreationKeepsNothing()
    {
        var state = await services.StartAsync();
        services.Provider.AddSession("code-1", new TestBankAccount("stable-1", "Compte courant", "EUR", 100));
        services.Provider.IsDown = true;

        var result = await FinishAsync("code-1", state);

        Assert.IsType<BankConnectionFinishResult.ProviderUnavailable>(result);
        Assert.Empty(services.Repository.SavedConnections);
    }

    // La banque ne répond pas à la lecture des soldes : rien n'est gardé, et la nouvelle session est révoquée.
    [Fact]
    public async Task BalanceFailureKeepsNothingAndRevokesTheSession()
    {
        var state = await services.StartAsync();
        var session = services.Provider.AddSession("code-1", new TestBankAccount("stable-1", "Compte courant", "EUR", 100));
        services.Provider.BalanceFailure = BankProviderFailure.Unavailable;

        var result = await FinishAsync("code-1", state);

        Assert.IsType<BankConnectionFinishResult.ProviderUnavailable>(result);
        Assert.Empty(services.Repository.SavedConnections);
        Assert.Contains(session.SessionId, services.Provider.RevokedSessions);
    }

    // Seuls les comptes en euros sont gardés : sans aucun, rien n'est gardé, et la session est révoquée.
    [Fact]
    public async Task SessionWithoutEuroAccountKeepsNothing()
    {
        var state = await services.StartAsync();
        var session = services.Provider.AddSession("code-1", new TestBankAccount("stable-1", "Compte en dollars", "USD", 100));

        var result = await FinishAsync("code-1", state);

        var refusal = Assert.IsType<BankConnectionFinishResult.NoAccountAdded>(result);
        Assert.Equal(new List<string> { "Compte en dollars" }, refusal.SkippedAccountNames);
        Assert.Empty(services.Repository.SavedConnections);
        Assert.Contains(session.SessionId, services.Provider.RevokedSessions);
    }

    // C'est la devise du compte qui compte : un compte en dollars reste écarté, même si la banque donne aussi son solde en euros.
    [Fact]
    public async Task AccountInAnotherCurrencyIsSkippedEvenWithAnEuroBalance()
    {
        var state = await services.StartAsync();
        var session = services.Provider.AddSession("code-1",
            new TestBankAccount("stable-1", "Compte courant", "EUR", 100),
            new TestBankAccount("stable-2", "Compte en dollars", "USD", 9000));
        var dollarAccount = session.Accounts.Single(account => account.StableId == "stable-2");
        services.Provider.BalancesByAccountUid[dollarAccount.Uid] = new List<ProvidedBalance> { new ProvidedBalance("CLBD", 8300, "EUR") };

        var result = await FinishAsync("code-1", state);

        var connected = Assert.IsType<BankConnectionFinishResult.Connected>(result);
        Assert.Equal(1, connected.AddedAccountCount);
        Assert.Equal(new List<string> { "Compte en dollars" }, connected.SkippedAccountNames);
        Assert.DoesNotContain(services.Repository.SavedAccounts, account => account.ExternalAccountId == "stable-2");
    }

    // Un compte déjà connecté par une autre connexion de l'utilisateur n'est pas ajouté deux fois : le solde total resterait juste.
    [Fact]
    public async Task AccountAlreadyConnectedElsewhereIsSkipped()
    {
        await services.ConnectAsync(new TestBankAccount("stable-1", "Compte courant", "EUR", 100));
        var state = await services.StartAsync();
        services.Provider.AddSession("code-2",
            new TestBankAccount("stable-1", "Compte courant", "EUR", 100),
            new TestBankAccount("stable-2", "Livret A", "EUR", 200));

        var result = await FinishAsync("code-2", state);

        var connected = Assert.IsType<BankConnectionFinishResult.Connected>(result);
        Assert.Equal(1, connected.AddedAccountCount);
        Assert.Equal(new List<string> { "Compte courant" }, connected.SkippedAccountNames);
        Assert.Single(services.Repository.SavedAccounts, account => account.ExternalAccountId == "stable-1");
    }

    // DD §5.1 : la fin de l'autorisation ne dépasse jamais 180 jours, même si le prestataire en donne plus.
    [Fact]
    public async Task AuthorizationEndNeverGoesBeyondOneHundredEightyDays()
    {
        var state = await services.StartAsync();
        services.Provider.SessionValidUntil = services.Now.AddDays(400);
        services.Provider.AddSession("code-1", new TestBankAccount("stable-1", "Compte courant", "EUR", 100));

        await FinishAsync("code-1", state);

        Assert.Equal(services.Now.AddDays(180), services.Repository.SavedConnections.Single().AuthorizationExpiresAt);
    }

    // Une session déjà finie ne sert à rien : rien n'est gardé, et elle est révoquée.
    [Fact]
    public async Task SessionAlreadyEndedKeepsNothing()
    {
        var state = await services.StartAsync();
        services.Provider.SessionValidUntil = services.Now;
        var session = services.Provider.AddSession("code-1", new TestBankAccount("stable-1", "Compte courant", "EUR", 100));

        var result = await FinishAsync("code-1", state);

        Assert.IsType<BankConnectionFinishResult.ProviderUnavailable>(result);
        Assert.Empty(services.Repository.SavedConnections);
        Assert.Contains(session.SessionId, services.Provider.RevokedSessions);
    }

    // GR Comptes : le renouvellement garde les mêmes comptes, avec leurs identifiants, met à jour leurs soldes,
    // ajoute les nouveaux, et révoque l'ancienne session.
    [Fact]
    public async Task RenewalKeepsTheSameAccounts()
    {
        var connection = await services.ConnectAsync(new TestBankAccount("stable-1", "Compte courant", "EUR", 100));
        var oldSessionId = connection.ExternalConnectionId;
        var oldAccountId = services.AccountsOf(connection).Single().Id;
        connection.MarkExpired();
        services.Clock.Advance(TimeSpan.FromDays(181));
        services.Provider.SessionValidUntil = services.Now.AddDays(180).AddMinutes(-10);
        await services.CreateStart().StartRenewalAsync(services.UserId, connection.Id);
        var state = services.Provider.AuthorizationRequests.Last().State;
        var newSession = services.Provider.AddSession("code-2",
            new TestBankAccount("stable-1", "Compte chèque", "EUR", 777),
            new TestBankAccount("stable-2", "Livret A", "EUR", 50));

        var result = await FinishAsync("code-2", state);

        var renewed = Assert.IsType<BankConnectionFinishResult.Renewed>(result);
        var accounts = services.AccountsOf(connection);
        Assert.Equal(connection.Id, renewed.ConnectionId);
        Assert.Equal(1, renewed.AddedAccountCount);
        Assert.Single(services.Repository.SavedConnections);
        Assert.Equal(newSession.SessionId, connection.ExternalConnectionId);
        Assert.Equal(BankConnectionStatus.Active, connection.Status);
        Assert.Equal(newSession.ValidUntil, connection.AuthorizationExpiresAt);
        Assert.Equal(777, accounts.Single(account => account.Id == oldAccountId).BalanceInCents);
        Assert.Equal("Compte chèque", accounts.Single(account => account.Id == oldAccountId).Name);
        Assert.Equal(2, accounts.Count);
        Assert.Contains(oldSessionId, services.Provider.RevokedSessions);
        Assert.Equal(2, CountEvents(SecurityEventType.BankConnection, SecurityEventResult.Success));
    }

    // La connexion à renouveler a été supprimée entre-temps : rien n'est créé, et la nouvelle session est révoquée.
    [Fact]
    public async Task RenewalOfADeletedConnectionKeepsNothing()
    {
        var connection = await services.ConnectAsync(new TestBankAccount("stable-1", "Compte courant", "EUR", 100));
        await services.CreateStart().StartRenewalAsync(services.UserId, connection.Id);
        var state = services.Provider.AuthorizationRequests.Last().State;
        var newSession = services.Provider.AddSession("code-2", new TestBankAccount("stable-1", "Compte courant", "EUR", 100));
        services.Repository.SavedConnections.Clear();
        services.Repository.SavedAccounts.Clear();

        var result = await FinishAsync("code-2", state);

        Assert.IsType<BankConnectionFinishResult.ConnectionGone>(result);
        Assert.Empty(services.Repository.SavedConnections);
        Assert.Contains(newSession.SessionId, services.Provider.RevokedSessions);
    }

    // Les conflits sans fin n'enregistrent rien, et la nouvelle session est révoquée.
    [Fact]
    public async Task EndlessConflictsKeepNothing()
    {
        var state = await services.StartAsync();
        var session = services.Provider.AddSession("code-1", new TestBankAccount("stable-1", "Compte courant", "EUR", 100));
        services.Repository.Conflicts = 10;

        var result = await FinishAsync("code-1", state);

        Assert.IsType<BankConnectionFinishResult.NotSaved>(result);
        Assert.Empty(services.Repository.SavedConnections);
        Assert.Contains(session.SessionId, services.Provider.RevokedSessions);
        Assert.Equal(0, CountEvents(SecurityEventType.BankConnection, SecurityEventResult.Success));
    }
}
