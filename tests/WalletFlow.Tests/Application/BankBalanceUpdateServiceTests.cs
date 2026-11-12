using WalletFlow.Application.BankConnections;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie la mise à jour des soldes, demandée par l'utilisateur ou faite en arrière-plan (GR Comptes, SS §6, DD §5).
public class BankBalanceUpdateServiceTests
{
    private readonly BankServices services = new BankServices();

    // Connecte la banque des tests avec un compte courant de 100,00 €.
    private async Task<BankConnection> ConnectAsync()
    {
        return await services.ConnectAsync(new TestBankAccount("stable-1", "Compte courant", "EUR", 10_000));
    }

    // GR Comptes : à l'ouverture de l'application, le solde est relu, avec sa date, et l'utilisateur étant là.
    [Fact]
    public async Task UserUpdateReadsTheNewBalanceWithItsDate()
    {
        var connection = await ConnectAsync();
        services.Provider.ChangeBalance("stable-1", 25_000);
        services.Clock.Advance(TimeSpan.FromHours(2));
        services.Provider.BalanceReadPresences.Clear();

        var result = await services.CreateUpdate().UpdateForUserAsync(services.UserId, services.Presence);

        var account = services.AccountsOf(connection).Single();
        Assert.IsType<BankUpdateResult.Updated>(result);
        Assert.Equal(25_000, account.BalanceInCents);
        Assert.Equal(services.Now, account.BalanceUpdatedAt);
        Assert.Equal(services.Presence, Assert.Single(services.Provider.BalanceReadPresences));
        Assert.Equal(BankConnectionStatus.Active, connection.Status);
    }

    // SS §6 : une mise à jour par minute et par utilisateur.
    [Fact]
    public async Task UserCanUpdateOnlyOncePerMinute()
    {
        await ConnectAsync();
        var update = services.CreateUpdate();

        Assert.IsType<BankUpdateResult.Updated>(await update.UpdateForUserAsync(services.UserId, services.Presence));
        Assert.IsType<BankUpdateResult.TooManyUpdates>(await update.UpdateForUserAsync(services.UserId, services.Presence));

        services.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.IsType<BankUpdateResult.Updated>(await update.UpdateForUserAsync(services.UserId, services.Presence));
    }

    // GR Comptes : si la banque ne répond pas, le dernier solde reste, avec sa date, et la connexion passe en erreur.
    [Fact]
    public async Task BankThatDoesNotAnswerKeepsTheLastBalance()
    {
        var connection = await ConnectAsync();
        var lastUpdate = services.AccountsOf(connection).Single().BalanceUpdatedAt;
        services.Clock.Advance(TimeSpan.FromHours(2));
        services.Provider.BalanceFailure = BankProviderFailure.Unavailable;

        await services.CreateUpdate().UpdateConnectionAsync(connection.Id, services.Presence);

        var account = services.AccountsOf(connection).Single();
        Assert.Equal(BankConnectionStatus.InError, connection.Status);
        Assert.Equal(10_000, account.BalanceInCents);
        Assert.Equal(lastUpdate, account.BalanceUpdatedAt);
    }

    // Une connexion en erreur redevient active dès que la banque répond.
    [Fact]
    public async Task ConnectionInErrorComesBackWhenTheBankAnswers()
    {
        var connection = await ConnectAsync();
        services.Provider.BalanceFailure = BankProviderFailure.Unavailable;
        await services.CreateUpdate().UpdateConnectionAsync(connection.Id, services.Presence);
        services.Provider.BalanceFailure = null;

        await services.CreateUpdate().UpdateConnectionAsync(connection.Id, services.Presence);

        Assert.Equal(BankConnectionStatus.Active, connection.Status);
    }

    // La banque retire l'autorisation avant sa date : la connexion expire, et ses comptes restent avec leur dernier solde.
    [Fact]
    public async Task SessionEndedAtTheBankExpiresTheConnection()
    {
        var connection = await ConnectAsync();
        services.Provider.EndedSessions.Add(connection.ExternalConnectionId);

        await services.CreateUpdate().UpdateConnectionAsync(connection.Id, services.Presence);

        Assert.Equal(BankConnectionStatus.Expired, connection.Status);
        Assert.Equal(10_000, services.AccountsOf(connection).Single().BalanceInCents);
    }

    // Une banque qui refuse pour l'instant ne change rien : le prochain passage réessaiera.
    [Fact]
    public async Task RateLimitedBankChangesNothing()
    {
        var connection = await ConnectAsync();
        services.Provider.BalanceFailure = BankProviderFailure.RateLimited;
        services.Provider.ChangeBalance("stable-1", 99);

        await services.CreateUpdate().UpdateConnectionAsync(connection.Id, null);

        Assert.Equal(BankConnectionStatus.Active, connection.Status);
        Assert.Equal(10_000, services.AccountsOf(connection).Single().BalanceInCents);
    }

    // L'autorisation est arrivée à sa date : la connexion expire sans demander quoi que ce soit au prestataire.
    [Fact]
    public async Task AuthorizationPastItsDateExpiresWithoutAskingTheProvider()
    {
        var connection = await ConnectAsync();
        services.Clock.Advance(TimeSpan.FromDays(181));
        services.Provider.BalanceReadPresences.Clear();

        await services.CreateUpdate().UpdateConnectionAsync(connection.Id, services.Presence);

        Assert.Equal(BankConnectionStatus.Expired, connection.Status);
        Assert.Empty(services.Provider.BalanceReadPresences);
    }

    [Fact]
    public async Task ExpiredConnectionIsNotUpdated()
    {
        var connection = await ConnectAsync();
        connection.MarkExpired();
        services.Provider.BalanceReadPresences.Clear();

        await services.CreateUpdate().UpdateConnectionAsync(connection.Id, services.Presence);

        Assert.Empty(services.Provider.BalanceReadPresences);
    }

    // La connexion a été renouvelée pendant la lecture : ce qui a été lu venait de l'ancienne session, rien n'est écrit.
    [Fact]
    public async Task ConnectionRenewedDuringTheReadingIsNotTouched()
    {
        var connection = await ConnectAsync();
        services.Provider.ChangeBalance("stable-1", 1);
        services.Provider.BeforeBalanceRead = () => EntityValues.WriteProperty(connection, nameof(BankConnection.ExternalConnectionId), "session-renouvelee");

        await services.CreateUpdate().UpdateConnectionAsync(connection.Id, services.Presence);

        Assert.Equal(10_000, services.AccountsOf(connection).Single().BalanceInCents);
    }

    // La connexion a été déconnectée juste avant l'enregistrement : rien n'est recréé.
    [Fact]
    public async Task ConnectionDeletedBeforeTheSaveStaysDeleted()
    {
        var connection = await ConnectAsync();
        services.Provider.BalanceFailure = BankProviderFailure.Unavailable;
        services.Repository.DeleteReadConnectionBeforeSave = true;

        await services.CreateUpdate().UpdateConnectionAsync(connection.Id, services.Presence);

        Assert.Empty(services.Repository.SavedConnections);
        Assert.Empty(services.Repository.SavedAccounts);
    }

    // GR Comptes : en arrière-plan, sans l'utilisateur, au plus une mise à jour toutes les 6 heures pour chaque connexion.
    [Fact]
    public async Task BackgroundUpdatesEachConnectionEverySixHoursWithoutTheUser()
    {
        var connection = await ConnectAsync();
        var background = services.CreateBackgroundUpdate();
        services.Provider.ChangeBalance("stable-1", 30_000);
        services.Provider.BalanceReadPresences.Clear();

        Assert.Equal(1, await background.UpdateDueConnectionsAsync());
        services.Clock.Advance(TimeSpan.FromHours(5));
        Assert.Equal(0, await background.UpdateDueConnectionsAsync());
        services.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, await background.UpdateDueConnectionsAsync());

        Assert.Equal(30_000, services.AccountsOf(connection).Single().BalanceInCents);
        Assert.Equal(2, services.Provider.BalanceReadPresences.Count);
        Assert.All(services.Provider.BalanceReadPresences, presence => Assert.Null(presence));
    }

    // Une connexion expirée n'est plus mise à jour en arrière-plan : il faut d'abord la renouveler.
    [Fact]
    public async Task BackgroundSkipsExpiredConnections()
    {
        var connection = await ConnectAsync();
        connection.MarkExpired();

        Assert.Equal(0, await services.CreateBackgroundUpdate().UpdateDueConnectionsAsync());
    }
}
