using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.BankData;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.EndToEnd;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Infrastructure.BankTestRows;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les connexions bancaires dans la vraie base PostgreSQL locale, avec l'utilisateur de l'API (GR Comptes, SS §4, MPD).
// Chaque test travaille dans une transaction annulée à la fin : la base reste comme avant. Seul le test de l'interblocage
// enregistre pour de vrai, car une autre transaction doit voir ses lignes : il efface son compte à la fin.
public class BankConnectionRepositoryTests
{
    private static readonly DateTime Now = BankTestRows.Now;
    private static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(30);

    // SS §4 : chaque recherche d'un utilisateur filtre sur l'utilisateur de la session.
    [Fact]
    public async Task ConnectionOfAnotherUserIsNeverFound()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var userA = await SaveAccountAsync(databaseContext, "test-banques-a@exemple.test");
        var userB = await SaveAccountAsync(databaseContext, "test-banques-b@exemple.test");
        var (connectionOfA, _) = await SaveConnectionAsync(databaseContext, userA.Id, "stable-a");
        var repository = new BankConnectionRepository(databaseContext);

        Assert.Null(await repository.FindConnectionAsync(userB.Id, connectionOfA.Id));
        Assert.Null(await repository.ReadExternalIdAsync(userB.Id, connectionOfA.Id));
        Assert.Empty(await repository.FindConnectionsAsync(userB.Id));
        Assert.Equal(connectionOfA.Id, Assert.Single(await repository.FindConnectionsAsync(userA.Id)).Id);
        Assert.NotNull(await repository.FindConnectionAsync(userA.Id, connectionOfA.Id));
    }

    // La déconnexion relit la session en base, jamais dans un objet lu plus tôt : un renouvellement entre-temps se voit.
    [Fact]
    public async Task ExternalIdIsReadAgainFromTheDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveAccountAsync(databaseContext, "test-banques-relecture@exemple.test");
        var (connection, _) = await SaveConnectionAsync(databaseContext, user.Id, "stable-1");
        var repository = new BankConnectionRepository(databaseContext);
        await repository.FindConnectionAsync(user.Id, connection.Id);

        await WriteAsAnotherRequestAsync(databaseContext, async () =>
            await databaseContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE connexion_bancaire SET id_connexion_externe = 'session-renouvelee' WHERE id_connexion = {connection.Id}"));

        Assert.Equal("session-renouvelee", await repository.ReadExternalIdAsync(user.Id, connection.Id));
    }

    [Fact]
    public async Task OnlyConnectionsThatAreNotExpiredAreUpdated()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveAccountAsync(databaseContext, "test-banques-etats@exemple.test");
        var (active, _) = await SaveConnectionAsync(databaseContext, user.Id, "stable-1", BankConnectionStatus.Active);
        var (inError, _) = await SaveConnectionAsync(databaseContext, user.Id, "stable-2", BankConnectionStatus.InError);
        var (expired, _) = await SaveConnectionAsync(databaseContext, user.Id, "stable-3", BankConnectionStatus.Expired);

        var connectionIds = await new BankConnectionRepository(databaseContext).FindConnectionIdsToUpdateAsync();

        Assert.Contains(active.Id, connectionIds);
        Assert.Contains(inError.Id, connectionIds);
        Assert.DoesNotContain(expired.Id, connectionIds);
    }

    // Un compte déjà présent est cherché dans les autres connexions du même utilisateur seulement.
    [Fact]
    public async Task AccountIsSearchedInTheOtherConnectionsOfTheSameUser()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var userA = await SaveAccountAsync(databaseContext, "test-banques-doublon-a@exemple.test");
        var userB = await SaveAccountAsync(databaseContext, "test-banques-doublon-b@exemple.test");
        var (connectionOfA, _) = await SaveConnectionAsync(databaseContext, userA.Id, "stable-a");
        await SaveConnectionAsync(databaseContext, userB.Id, "stable-b");
        var repository = new BankConnectionRepository(databaseContext);

        Assert.True(await repository.IsAccountConnectedElsewhereAsync(userA.Id, "stable-a", null));
        Assert.False(await repository.IsAccountConnectedElsewhereAsync(userA.Id, "stable-a", connectionOfA.Id));
        Assert.False(await repository.IsAccountConnectedElsewhereAsync(userB.Id, "stable-a", null));
        Assert.False(await repository.IsAccountConnectedElsewhereAsync(userA.Id, "stable-b", null));
    }

    // GR Comptes : déconnecter une banque supprime ses comptes et leurs transactions. Les catégories restent.
    [Fact]
    public async Task DeletionRemovesTheAccountsAndTheirTransactions()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveAccountAsync(databaseContext, "test-banques-suppression@exemple.test");
        var (connection, account) = await SaveConnectionAsync(databaseContext, user.Id, "stable-1");
        var leisure = await databaseContext.Categories.AsNoTracking().SingleAsync(row => row.UserId == user.Id && row.Name == "Loisirs");
        databaseContext.BankTransactions.Add(new BankTransaction
        {
            ExternalTransactionId = "operation-1",
            TransactionDate = new DateOnly(2026, 11, 10),
            Label = "CINEMA",
            AmountInCents = -1200,
            BankAccountId = account.Id,
            CategoryId = leisure.Id
        });
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        var isDeleted = await new BankConnectionRepository(databaseContext).DeleteAsync(user.Id, connection.Id, connection.ExternalConnectionId);

        await databaseContext.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
        Assert.True(isDeleted);
        Assert.False(await databaseContext.BankConnections.AnyAsync(row => row.Id == connection.Id));
        Assert.False(await databaseContext.BankAccounts.AnyAsync(row => row.Id == account.Id));
        Assert.False(await databaseContext.BankTransactions.AnyAsync(row => row.BankAccountId == account.Id));
        Assert.True(await databaseContext.Categories.AnyAsync(row => row.Id == leisure.Id));
    }

    // La suppression n'a lieu que si la session est toujours celle qui vient d'être révoquée, et pour son propriétaire.
    [Fact]
    public async Task DeletionRefusesAChangedSessionAndAnotherUser()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveAccountAsync(databaseContext, "test-banques-refus@exemple.test");
        var (connection, _) = await SaveConnectionAsync(databaseContext, user.Id, "stable-1");
        var repository = new BankConnectionRepository(databaseContext);

        Assert.False(await repository.DeleteAsync(user.Id, connection.Id, "session-revoquee-avant"));
        Assert.False(await repository.DeleteAsync(Guid.NewGuid(), connection.Id, connection.ExternalConnectionId));
        Assert.True(await databaseContext.BankConnections.AnyAsync(row => row.Id == connection.Id));
    }

    // La suppression est une écriture immédiate : elle n'emporte jamais des changements préparés pour l'unité de travail.
    [Fact]
    public async Task DeletionRefusesToCarryPendingChanges()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveAccountAsync(databaseContext, "test-banques-attente@exemple.test");
        var (connection, _) = await SaveConnectionAsync(databaseContext, user.Id, "stable-1");
        var repository = new BankConnectionRepository(databaseContext);
        repository.PrepareAccount(new BankAccount { ExternalAccountId = "stable-2", Name = "Livret A", BankConnectionId = connection.Id, BalanceUpdatedAt = Now });

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.DeleteAsync(user.Id, connection.Id, connection.ExternalConnectionId));
    }

    // Deux mises à jour du même solde au même instant : la seconde échoue, relit, et recommence (xmin sur compte_bancaire).
    [Fact]
    public async Task BalanceWrittenMeanwhileMakesTheSaveAConflict()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveAccountAsync(databaseContext, "test-banques-version@exemple.test");
        var (connection, _) = await SaveConnectionAsync(databaseContext, user.Id, "stable-1");
        var repository = new BankConnectionRepository(databaseContext);
        var account = (await repository.FindAccountsAsync(new List<Guid> { connection.Id })).Single();
        account.UpdateBalance(5_000, Now.AddHours(1));

        await WriteAsAnotherRequestAsync(databaseContext, async () =>
            await databaseContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE compte_bancaire SET solde_compte = 7000 WHERE id_compte = {account.Id}"));

        Assert.False(await TrySaveChangesAsync(databaseContext));
        Assert.Equal(7000, (await databaseContext.BankAccounts.AsNoTracking().SingleAsync(row => row.Id == account.Id)).BalanceInCents);
    }

    // Un vrai interblocage : une autre transaction verrouille la connexion, l'enregistrement change le compte puis attend la connexion,
    // et l'autre transaction demande alors le compte. Chacune attend l'autre : chacune vérifie les interblocages quand son attente
    // dure depuis le délai de PostgreSQL, et la première qui vérifie est arrêtée. L'autre transaction attend la moitié de ce délai
    // avant de demander le compte : l'enregistrement vérifie bien avant elle, et c'est lui qui est arrêté, à coup sûr.
    // L'unité de travail y voit un conflit à rejouer, pas une panne, et rien n'est écrit.
    [Fact]
    public async Task DeadlockDuringASaveIsAConflict()
    {
        var email = TestAccountCleanup.CreateUniqueEmail("test-banques-interblocage");

        try
        {
            await using var databaseContext = CreateDatabaseContext();
            var user = await SaveConfirmedAccountAsync(databaseContext, email);
            var (savedConnection, savedAccount) = await SaveConnectionAsync(databaseContext, user.Id, "stable-1");
            var repository = new BankConnectionRepository(databaseContext);
            var connection = await repository.FindConnectionAsync(user.Id, savedConnection.Id);
            var account = (await repository.FindAccountsAsync(new List<Guid> { savedConnection.Id })).Single();
            account.UpdateBalance(5_000, Now.AddHours(1));
            connection!.RecordBankFailure();

            await databaseContext.Database.OpenConnectionAsync();
            var savingProcessId = ((NpgsqlConnection)databaseContext.Database.GetDbConnection()).ProcessID;
            await using var otherConnection = new NpgsqlConnection(ReadConnectionString());
            await otherConnection.OpenAsync();
            await using var otherTransaction = await otherConnection.BeginTransactionAsync();
            await LockRowAsync(otherTransaction, "SELECT 1 FROM connexion_bancaire WHERE id_connexion = @id FOR UPDATE", savedConnection.Id);

            var detectionDelay = await ReadDeadlockDetectionDelayAsync(otherTransaction);
            var saving = TrySaveChangesAsync(databaseContext);
            await WaitUntilBlockedAsync(otherTransaction, savingProcessId);
            await Task.Delay(detectionDelay / 2);
            await LockRowAsync(otherTransaction, "SELECT 1 FROM compte_bancaire WHERE id_compte = @id FOR UPDATE", savedAccount.Id);
            await otherTransaction.RollbackAsync();

            Assert.False(await saving.WaitAsync(MaximumWait));
            Assert.Empty(databaseContext.ChangeTracker.Entries());
            Assert.Equal(10_000, (await databaseContext.BankAccounts.AsNoTracking().SingleAsync(row => row.Id == savedAccount.Id)).BalanceInCents);
            Assert.Equal(BankConnectionStatus.Active, (await databaseContext.BankConnections.AsNoTracking().SingleAsync(row => row.Id == savedConnection.Id)).Status);
        }
        finally
        {
            await TestAccountCleanup.DeleteAccountAsync(email);
        }
    }

    // Le même interblocage pendant une déconnexion : une autre transaction verrouille le compte, la suppression prend la connexion
    // puis attend le compte (ON DELETE CASCADE), et l'autre transaction demande alors la connexion. PostgreSQL arrête la suppression :
    // le dépôt répond que rien n'est supprimé, et la déconnexion recommence (BankDisconnectionService).
    [Fact]
    public async Task DeadlockDuringADeletionDeletesNothing()
    {
        var email = TestAccountCleanup.CreateUniqueEmail("test-banques-interblocage-suppression");

        try
        {
            await using var databaseContext = CreateDatabaseContext();
            var user = await SaveConfirmedAccountAsync(databaseContext, email);
            var (connection, account) = await SaveConnectionAsync(databaseContext, user.Id, "stable-1");

            await databaseContext.Database.OpenConnectionAsync();
            var deletingProcessId = ((NpgsqlConnection)databaseContext.Database.GetDbConnection()).ProcessID;
            await using var otherConnection = new NpgsqlConnection(ReadConnectionString());
            await otherConnection.OpenAsync();
            await using var otherTransaction = await otherConnection.BeginTransactionAsync();
            await LockRowAsync(otherTransaction, "SELECT 1 FROM compte_bancaire WHERE id_compte = @id FOR UPDATE", account.Id);

            var detectionDelay = await ReadDeadlockDetectionDelayAsync(otherTransaction);
            var deleting = new BankConnectionRepository(databaseContext).DeleteAsync(user.Id, connection.Id, connection.ExternalConnectionId);
            await WaitUntilBlockedAsync(otherTransaction, deletingProcessId);
            await Task.Delay(detectionDelay / 2);
            await LockRowAsync(otherTransaction, "SELECT 1 FROM connexion_bancaire WHERE id_connexion = @id FOR UPDATE", connection.Id);
            await otherTransaction.RollbackAsync();

            Assert.False(await deleting.WaitAsync(MaximumWait));
            Assert.True(await databaseContext.BankConnections.AnyAsync(row => row.Id == connection.Id));
            Assert.True(await databaseContext.BankAccounts.AnyAsync(row => row.Id == account.Id));
        }
        finally
        {
            await TestAccountCleanup.DeleteAccountAsync(email);
        }
    }

    // Verrouille une ligne dans l'autre transaction, en attendant si une autre transaction la tient déjà.
    private static async Task LockRowAsync(NpgsqlTransaction otherTransaction, string lockQuery, Guid rowId)
    {
        await using var command = new NpgsqlCommand(lockQuery, otherTransaction.Connection, otherTransaction);
        command.Parameters.AddWithValue("id", rowId);
        await command.ExecuteScalarAsync();
    }

    // Lit le délai d'attente après lequel PostgreSQL vérifie les interblocages (deadlock_timeout, une seconde par défaut).
    private static async Task<TimeSpan> ReadDeadlockDetectionDelayAsync(NpgsqlTransaction otherTransaction)
    {
        await using var command = new NpgsqlCommand(
            "SELECT setting::int FROM pg_settings WHERE name = 'deadlock_timeout' AND unit = 'ms'",
            otherTransaction.Connection,
            otherTransaction);
        var milliseconds = (int)(await command.ExecuteScalarAsync())!;

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    // Attend que PostgreSQL montre l'enregistrement bloqué par l'autre transaction.
    private static async Task WaitUntilBlockedAsync(NpgsqlTransaction otherTransaction, int savingProcessId)
    {
        var deadline = DateTime.UtcNow + MaximumWait;

        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand("SELECT @other = ANY(pg_blocking_pids(@saving))", otherTransaction.Connection, otherTransaction);
            command.Parameters.AddWithValue("other", otherTransaction.Connection!.ProcessID);
            command.Parameters.AddWithValue("saving", savingProcessId);

            if ((bool)(await command.ExecuteScalarAsync())!)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("L'enregistrement n'a jamais attendu l'autre transaction.");
    }

    // Chaîne réelle : connexion, mise à jour et déconnexion avec le vrai dépôt, la vraie unité de travail, le vrai journal
    // et la vraie base. Seul le prestataire est faux.
    [Fact]
    public async Task ConnectionLivesItsWholeLifeOnTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveAccountAsync(databaseContext, "test-banques-chaine@exemple.test");
        var clock = new FakeTimeProvider();
        var provider = new FakeBankDataProvider { SessionValidUntil = clock.GetUtcNow().UtcDateTime.AddDays(179) };
        var repository = new BankConnectionRepository(databaseContext);
        var store = new InMemoryBankAuthorizationStore(clock);
        var tokens = new RandomSecureTokenGenerator();
        var journal = CreateSecurityJournal(databaseContext, clock);
        var presence = new UserPresence(IPAddress.Parse("203.0.113.7"), "Navigateur de test");
        var arrival = new BankTransactionArrival(new TransactionRepository(databaseContext), new CategoryRepository(databaseContext));

        await new BankAuthorizationStartService(provider, repository, store, tokens, new PublicWebsiteSettings("https://walletflow.test"), clock)
            .StartNewAsync(user.Id, FakeBankDataProvider.BankName);
        provider.AddSession("code-1", new TestBankAccount("stable-1", "Compte courant", "EUR", 10_000));
        var finished = await new BankConnectionFinishService(provider, repository, store, tokens, arrival, CreateSavingService(databaseContext), journal, clock)
            .FinishAsync(user.Id, "code-1", provider.AuthorizationRequests.Single().State, presence);
        var connectionId = Assert.IsType<BankConnectionFinishResult.Connected>(finished).ConnectionId;

        provider.ChangeBalance("stable-1", 42_000);
        clock.Advance(TimeSpan.FromHours(1));
        await new BankBalanceUpdateService(provider, repository, new InMemoryBankUpdateLimiter(clock), arrival, CreateSavingService(databaseContext), clock)
            .UpdateForUserAsync(user.Id, presence);
        var updatedAccount = await databaseContext.BankAccounts.AsNoTracking().SingleAsync(row => row.BankConnectionId == connectionId);

        var disconnected = await new BankDisconnectionService(provider, repository, journal)
            .DisconnectAsync(user.Id, connectionId, presence.IpAddress);

        Assert.Equal(42_000, updatedAccount.BalanceInCents);
        Assert.IsType<BankDisconnectionResult.Disconnected>(disconnected);
        Assert.False(await databaseContext.BankConnections.AnyAsync(row => row.Id == connectionId));
        Assert.Equal(1, await databaseContext.SecurityEvents.CountAsync(row =>
            row.UserId == user.Id && row.Type == SecurityEventType.BankConnection && row.Result == SecurityEventResult.Success));
        Assert.Equal(1, await databaseContext.SecurityEvents.CountAsync(row =>
            row.UserId == user.Id && row.Type == SecurityEventType.BankDisconnection && row.Result == SecurityEventResult.Success));
    }
}
