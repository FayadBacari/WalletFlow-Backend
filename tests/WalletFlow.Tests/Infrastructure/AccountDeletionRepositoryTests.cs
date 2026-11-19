using System.Net;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Emails;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.BankTestRows;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la suppression d'un compte dans la vraie base (GR Suppression, SS §7, MPD) : l'effacement emporte tout ce qui
// appartient au compte et garde son journal sans lui, la version du compte arrête un effacement après une connexion, la demande
// efface tous les liens, et les tâches de fond laissent de côté les comptes en suppression. Chaque test annule sa transaction.
public class AccountDeletionRepositoryTests
{
    private static readonly IPAddress IpAddress = IPAddress.Parse("203.0.113.70");

    // Une empreinte de 64 caractères hexadécimaux, unique, comme celles des jetons et des appareils.
    private static string CreateHash()
    {
        return Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    }

    // Compte les lignes qui appartiennent encore à ce compte, table par table.
    private static async Task<List<int>> CountRowsOfAsync(WalletFlowDatabaseContext databaseContext, FullAccountRows rows)
    {
        var userId = rows.Account.Id;
        var categoryIds = rows.Categories.Select(category => category.Id).ToList();
        var bagIds = rows.Bags.Select(bag => bag.Id).ToList();

        return new List<int>
        {
            await databaseContext.Users.CountAsync(user => user.Id == userId),
            await databaseContext.Categories.CountAsync(category => category.UserId == userId),
            await databaseContext.CategorizationRules.CountAsync(rule => categoryIds.Contains(rule.CategoryId)),
            await databaseContext.BankConnections.CountAsync(connection => connection.UserId == userId),
            await databaseContext.BankAccounts.CountAsync(account => account.BankConnectionId == rows.Connection.Id),
            await databaseContext.BankTransactions.CountAsync(operation => operation.BankAccountId == rows.BankAccount.Id),
            await databaseContext.Bags.CountAsync(bag => bag.UserId == userId),
            await databaseContext.BagMovements.CountAsync(movement =>
                (movement.SourceBagId != null && bagIds.Contains(movement.SourceBagId.Value))
                || (movement.DestinationBagId != null && bagIds.Contains(movement.DestinationBagId.Value))),
            await databaseContext.SavingsSuggestions.CountAsync(suggestion => suggestion.UserId == userId),
            await databaseContext.AlertPreferences.CountAsync(preference => preference.UserId == userId),
            await databaseContext.Alerts.CountAsync(alert => alert.UserId == userId),
            await databaseContext.SupportRequests.CountAsync(request => request.UserId == userId),
            await databaseContext.SupportResponses.CountAsync(response => response.SupportRequestId == rows.Request.Id),
            await databaseContext.UserSessions.CountAsync(session => session.UserId == userId),
            await databaseContext.Devices.CountAsync(device => device.UserId == userId),
            await databaseContext.EmailTokens.CountAsync(token => token.UserId == userId),
            await databaseContext.BackupCodes.CountAsync(code => code.UserId == userId),
            await databaseContext.EmailRequests.CountAsync(request => request.UserId == userId)
        };
    }

    // Ajoute au compte ce que FullAccountRows n'a pas : une session, un appareil, un lien, un code de secours, un e-mail en
    // attente et un événement du journal.
    private static async Task<SecurityEvent> SaveSecurityRowsAsync(WalletFlowDatabaseContext databaseContext, Guid userId)
    {
        var ownEvent = new SecurityEvent
        {
            Type = SecurityEventType.Login,
            Result = SecurityEventResult.Success,
            OccurredAt = Now,
            IpAddress = IpAddress,
            UserId = userId
        };
        databaseContext.SecurityEvents.Add(ownEvent);
        databaseContext.UserSessions.Add(new UserSession { CreatedAt = Now, Id = CreateHash(), LastActivityAt = Now, UserId = userId });
        databaseContext.Devices.Add(new Device { DeviceToken = CreateHash(), UserId = userId, LastUsedAt = Now });
        databaseContext.EmailTokens.Add(new EmailToken { Type = EmailTokenType.PasswordRecovery, TokenHash = CreateHash(), ExpiresAt = Now.AddMinutes(30), UserId = userId });
        databaseContext.BackupCodes.Add(new BackupCode { CodeHash = "empreinte-de-code-de-test", UserId = userId });
        databaseContext.EmailRequests.Add(EmailRequest.ForNewDevice(userId, IpAddress, Now));
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        return ownEvent;
    }

    // GR Suppression : l'effacement emporte chaque ligne du compte, et rien d'un autre compte. Son journal reste, sans son
    // identifiant ni son adresse IP ; l'action d'un administrateur sur lui ne le désigne plus. L'e-mail d'effacement, préparé
    // dans la même transaction, reste dans la file avec l'adresse.
    [Fact]
    public async Task ErasureTakesEveryRowOfTheAccountAndKeepsItsJournalWithoutIt()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var erased = await FullAccountRows.SaveAsync(databaseContext, "test-effacement@exemple.test", "stable-effacement");
        var kept = await FullAccountRows.SaveAsync(databaseContext, "test-effacement-autre@exemple.test", "stable-effacement-autre");
        var ownEvent = await SaveSecurityRowsAsync(databaseContext, erased.Account.Id);
        await SaveSecurityRowsAsync(databaseContext, kept.Account.Id);
        var adminAction = new SecurityEvent
        {
            Type = SecurityEventType.AdminAction,
            Result = SecurityEventResult.Success,
            OccurredAt = Now,
            IpAddress = IpAddress,
            UserId = kept.Account.Id,
            TargetUserId = erased.Account.Id
        };
        databaseContext.SecurityEvents.Add(adminAction);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();
        var keptRowsBefore = await CountRowsOfAsync(databaseContext, kept);
        var accounts = new UserAccountRepository(databaseContext);

        var account = await accounts.FindByIdAsync(erased.Account.Id);
        accounts.PrepareErasure(account!);
        new EmailRequestRepository(databaseContext, new EmailRequestWakeUp()).PrepareRequest(EmailRequest.ForAccountErasure(account!.Email, Now));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.True(saved);
        databaseContext.ChangeTracker.Clear();
        Assert.All(await CountRowsOfAsync(databaseContext, erased), count => Assert.Equal(0, count));
        Assert.Equal(keptRowsBefore, await CountRowsOfAsync(databaseContext, kept));
        var anonymous = await databaseContext.SecurityEvents.SingleAsync(row => row.Id == ownEvent.Id);
        Assert.Equal(((Guid?)null, (IPAddress?)null), (anonymous.UserId, anonymous.IpAddress));
        var action = await databaseContext.SecurityEvents.SingleAsync(row => row.Id == adminAction.Id);
        Assert.Equal(((Guid?)kept.Account.Id, (Guid?)null, IpAddress), (action.UserId, action.TargetUserId, action.IpAddress));
        Assert.True(await databaseContext.EmailRequests.AnyAsync(request =>
            request.Type == EmailRequestType.AccountErasure
            && request.EmailAddress == "test-effacement@exemple.test"
            && request.UserId == null));
    }

    // Une connexion qui annule la suppression entre la lecture du compte et son effacement change sa version : l'effacement
    // échoue, et le compte reste.
    [Fact]
    public async Task ErasureFailsWhenTheAccountChangedSinceItsReading()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var saved = await SaveAccountAsync(databaseContext, "test-effacement-annule@exemple.test");
        await databaseContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE utilisateur SET date_demande_suppression = {Now.AddDays(-30)} WHERE id_utilisateur = {saved.Id}");
        var accounts = new UserAccountRepository(databaseContext);
        var account = await accounts.FindByIdAsync(saved.Id);

        await WriteAsAnotherRequestAsync(databaseContext, () => databaseContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE utilisateur SET date_demande_suppression = NULL WHERE id_utilisateur = {saved.Id}"));
        accounts.PrepareErasure(account!);

        Assert.False(await TrySaveChangesAsync(databaseContext));
        databaseContext.ChangeTracker.Clear();
        Assert.True(await databaseContext.Users.AnyAsync(user => user.Id == saved.Id));
    }

    // SS §7 : la demande efface tous les liens du compte, quel que soit leur type, et seulement les siens.
    [Fact]
    public async Task EveryLinkOfTheAccountIsRemoved()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountAsync(databaseContext, "test-liens-suppression@exemple.test");
        var other = await SaveAccountAsync(databaseContext, "test-liens-suppression-autre@exemple.test");
        databaseContext.EmailTokens.AddRange(
            new EmailToken { Type = EmailTokenType.PasswordRecovery, TokenHash = CreateHash(), ExpiresAt = Now.AddMinutes(30), UserId = account.Id },
            new EmailToken { Type = EmailTokenType.EmailChange, TokenHash = CreateHash(), NewEmail = "nouvelle-suppression@exemple.test", ExpiresAt = Now.AddHours(24), UserId = account.Id },
            new EmailToken { Type = EmailTokenType.TwoFactorDeactivation, TokenHash = CreateHash(), ExpiresAt = Now.AddHours(24), UserId = account.Id },
            new EmailToken { Type = EmailTokenType.PasswordRecovery, TokenHash = CreateHash(), ExpiresAt = Now.AddMinutes(30), UserId = other.Id });
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        await new UserAccountRepository(databaseContext).PrepareAllLinksRemovalAsync(account.Id);

        Assert.True(await TrySaveChangesAsync(databaseContext));
        Assert.Equal(0, await databaseContext.EmailTokens.CountAsync(token => token.UserId == account.Id));
        Assert.Equal(1, await databaseContext.EmailTokens.CountAsync(token => token.UserId == other.Id));
    }

    // Un compte est à effacer 30 jours après sa demande, pile : celui demandé à la limite l'est, une microseconde après non. Le
    // plus ancien vient d'abord, et un compte sans demande n'y est jamais.
    [Fact]
    public async Task AccountsToEraseAreThoseRequestedUpToTheLimitOldestFirst()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var limit = Now.AddDays(-30);
        var older = await SaveAccountAsync(databaseContext, "test-effacer-ancien@exemple.test");
        var atTheLimit = await SaveAccountAsync(databaseContext, "test-effacer-limite@exemple.test");
        var tooRecent = await SaveAccountAsync(databaseContext, "test-effacer-recent@exemple.test");
        var notRequested = await SaveAccountAsync(databaseContext, "test-effacer-sans-demande@exemple.test");
        foreach (var (userId, requestedAt) in new[] { (older.Id, limit.AddDays(-1)), (atTheLimit.Id, limit), (tooRecent.Id, limit.AddTicks(10)) })
        {
            await databaseContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE utilisateur SET date_demande_suppression = {requestedAt} WHERE id_utilisateur = {userId}");
        }

        var toErase = await new UserAccountRepository(databaseContext).FindAccountsToEraseAsync(limit);

        var testIds = new[] { older.Id, atTheLimit.Id, tooRecent.Id, notRequested.Id };
        Assert.Equal(new List<Guid> { older.Id, atTheLimit.Id }, toErase.Where(testIds.Contains).ToList());
    }

    // Les tâches de fond laissent de côté les comptes en suppression : pas de mise à jour bancaire, pas de calcul de suggestions,
    // pas d'alerte d'autorisation. Seule la révocation s'occupe de leurs connexions pas encore expirées.
    [Fact]
    public async Task BackgroundTasksLeaveAccountsBeingDeletedAside()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var active = await FullAccountRows.SaveAsync(databaseContext, "test-taches-actif@exemple.test", "stable-taches-actif");
        var leaving = await FullAccountRows.SaveAsync(databaseContext, "test-taches-suppression@exemple.test", "stable-taches-suppression");
        var (expiredConnection, _) = await SaveConnectionAsync(databaseContext, leaving.Account.Id, "stable-taches-expire", BankConnectionStatus.Expired);
        await databaseContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE utilisateur SET date_demande_suppression = {Now}, dernier_mois_suggestions = NULL WHERE id_utilisateur = {leaving.Account.Id}");
        await databaseContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE utilisateur SET dernier_mois_suggestions = NULL WHERE id_utilisateur = {active.Account.Id}");
        var testConnections = new[] { active.Connection.Id, leaving.Connection.Id, expiredConnection.Id };
        var testUsers = new[] { active.Account.Id, leaving.Account.Id };
        var banks = new BankConnectionRepository(databaseContext);

        var toUpdate = (await banks.FindConnectionIdsToUpdateAsync()).Where(testConnections.Contains).ToList();
        var toRevoke = (await banks.FindConnectionIdsToRevokeAsync()).Where(testConnections.Contains).ToList();
        var toCalculate = (await new SuggestionRepository(databaseContext).FindUsersToCalculateAsync(new DateOnly(2026, 10, 1), null, 100000))
            .Where(testUsers.Contains)
            .ToList();
        var toWarn = (await new AlertRepository(databaseContext).FindConnectionsToWarnAsync(Now.AddYears(1))).Where(testConnections.Contains).ToList();

        Assert.Equal(new List<Guid> { active.Connection.Id }, toUpdate);
        Assert.Equal(new List<Guid> { leaving.Connection.Id }, toRevoke);
        Assert.Equal(new List<Guid> { active.Account.Id }, toCalculate);
        Assert.Equal(new List<Guid> { active.Connection.Id }, toWarn);
    }
}
