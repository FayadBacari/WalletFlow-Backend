using System.Net;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les sessions, les appareils et les compteurs de connexion dans la vraie base PostgreSQL locale,
// avec l'utilisateur de l'API. Chaque test travaille dans une transaction annulée à la fin : la base reste vide.
public class UserSessionRepositoryTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    // Enregistre un nouveau compte confirmé avec cette adresse, puis oublie les objets gardés en mémoire.
    private static async Task<User> SaveConfirmedAccountAsync(
        WalletFlowDatabaseContext databaseContext,
        string emailAddress)
    {
        var newAccount = new User
        {
            Email = emailAddress,
            PasswordHash = "empreinte-de-test",
            IsEmailConfirmed = true
        };
        databaseContext.Users.Add(newAccount);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        return newAccount;
    }

    // Crée une session sans l'enregistrer, avec une empreinte de la taille d'un vrai SHA-256.
    private static UserSession CreateSession(User account, DateTime lastActivityAt)
    {
        return new UserSession
        {
            Id = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + new string('0', 32),
            LastActivityAt = lastActivityAt,
            UserId = account.Id
        };
    }

    // Crée l'événement d'une connexion réussie, sans l'enregistrer.
    private static SecurityEvent CreateLoginEvent(User account)
    {
        return new SecurityEvent
        {
            Type = SecurityEventType.Login,
            Result = SecurityEventResult.Success,
            OccurredAt = Now,
            IpAddress = IPAddress.Parse("203.0.113.7"),
            UserId = account.Id
        };
    }

    // Prépare une connexion réussie comme le fait le cas d'utilisation : compteurs remis à zéro, compte contrôlé,
    // nouvelle session, nouvel appareil s'il y en a un, et événement du journal. Rien n'est encore écrit.
    private static void PrepareLogin(
        WalletFlowDatabaseContext databaseContext,
        User account,
        UserSession session,
        Device? device,
        SecurityEvent securityEvent)
    {
        var sessionRepository = new UserSessionRepository(databaseContext);

        account.ResetLoginCounters();
        new UserAccountRepository(databaseContext).PrepareUnchangedCheck(account);
        sessionRepository.PrepareSession(session);

        if (device != null)
        {
            sessionRepository.PrepareDevice(device);
        }

        new SecurityEventRepository(databaseContext).PrepareEvent(securityEvent);
    }

    // Enregistre directement des sessions déjà créées, puis oublie les objets gardés en mémoire.
    private static async Task SaveSessionsDirectlyAsync(WalletFlowDatabaseContext databaseContext, params UserSession[] sessions)
    {
        databaseContext.UserSessions.AddRange(sessions);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();
    }

    // Une autre requête change le compteur directement en base : les objets gardés en mémoire ne le voient pas.
    private static async Task SimulateConcurrentAttemptAsync(WalletFlowDatabaseContext databaseContext, Guid accountId, short failedAttemptCount)
    {
        await WriteAsAnotherRequestAsync(databaseContext, () => databaseContext.Users
            .Where(row => row.Id == accountId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                row => row.FailedLoginAttemptCount,
                failedAttemptCount)));
    }

    [Fact]
    public async Task SuccessfulLoginIsReallySavedWithItsCountersSessionDeviceAndEvent()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-session-connexion@exemple.test");
        var account = await databaseContext.Users.SingleAsync(row => row.Id == savedAccount.Id);
        var session = CreateSession(savedAccount, Now);
        var device = new Device { DeviceToken = new string('a', 64), UserId = savedAccount.Id };

        PrepareLogin(databaseContext, account, session, device, CreateLoginEvent(savedAccount));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.True(saved);

        // On oublie les objets en mémoire : tout ce qui suit est vraiment relu dans la base.
        databaseContext.ChangeTracker.Clear();
        var reloadedSession = await databaseContext.UserSessions.SingleAsync(row =>
            row.UserId == savedAccount.Id);
        Assert.Equal(session.Id, reloadedSession.Id);
        Assert.Equal(Now, reloadedSession.LastActivityAt);

        var reloadedDevice = await databaseContext.Devices.SingleAsync(row =>
            row.UserId == savedAccount.Id);
        Assert.Equal(new string('a', 64), reloadedDevice.DeviceToken);

        var reloadedEvent = await databaseContext.SecurityEvents.SingleAsync(row =>
            row.UserId == savedAccount.Id);
        Assert.Equal(SecurityEventType.Login, reloadedEvent.Type);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), reloadedEvent.IpAddress);
    }

    [Fact]
    public async Task SuccessfulLoginWithoutNewDeviceSavesNoDevice()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-session-sans-appareil@exemple.test");
        var account = await new UserAccountRepository(databaseContext).FindByIdAsync(savedAccount.Id);

        PrepareLogin(databaseContext, account!, CreateSession(savedAccount, Now), null, CreateLoginEvent(savedAccount));
        await TrySaveChangesAsync(databaseContext);

        Assert.Equal(0, await databaseContext.Devices.CountAsync(row => row.UserId == savedAccount.Id));
    }

    // Tout ou rien : un conflit sur le compte n'enregistre ni la session, ni l'appareil, ni l'événement.
    // Le même enregistrement doit ensuite réussir, une fois le compte relu.
    [Fact]
    public async Task ConflictOnTheCountersSavesNothingOfTheLoginAndTheLoginCanBeSavedAfterwards()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var accountRepository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-session-conflit@exemple.test");
        var account = await accountRepository.FindByIdAsync(savedAccount.Id);
        await SimulateConcurrentAttemptAsync(databaseContext, savedAccount.Id, 1);
        var session = CreateSession(savedAccount, Now);
        var device = new Device { DeviceToken = new string('b', 64), UserId = savedAccount.Id };
        var securityEvent = CreateLoginEvent(savedAccount);

        // Le compteur lu est 0, mais la base contient 1 : cette écriture doit être refusée.
        PrepareLogin(databaseContext, account!, session, device, securityEvent);
        var firstSaved = await TrySaveChangesAsync(databaseContext);

        Assert.False(firstSaved);
        Assert.Equal(0, await databaseContext.UserSessions.CountAsync(row => row.UserId == savedAccount.Id));
        Assert.Equal(0, await databaseContext.Devices.CountAsync(row => row.UserId == savedAccount.Id));
        Assert.Equal(0, await databaseContext.SecurityEvents.CountAsync(row => row.UserId == savedAccount.Id));

        var reloadedAccount = await accountRepository.FindByIdAsync(savedAccount.Id);
        Assert.Equal(1, reloadedAccount!.FailedLoginAttemptCount);
        Assert.Equal(0, reloadedAccount.LockoutCount);

        PrepareLogin(databaseContext, reloadedAccount, session, device, securityEvent);
        var secondSaved = await TrySaveChangesAsync(databaseContext);

        Assert.True(secondSaved);
        Assert.Equal(1, await databaseContext.UserSessions.CountAsync(row => row.UserId == savedAccount.Id));
    }

    [Fact]
    public async Task LoginCountersAreReallySavedInTheDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var accountRepository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-compteurs-enregistres@exemple.test");
        await databaseContext.Users
            .Where(row => row.Id == savedAccount.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.FailedLoginAttemptCount, (short)4)
                .SetProperty(row => row.LockoutCount, (short)7));
        var account = await accountRepository.FindByIdAsync(savedAccount.Id);

        // Le 5e essai bloque le compte pour la 8e fois : 15 minutes × 2^7 dépasse 24 heures, le blocage dure donc 24 heures.
        account!.CountLoginAttempt(Now);
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.True(saved);
        databaseContext.ChangeTracker.Clear();
        var reloadedAccount = await accountRepository.FindByIdAsync(savedAccount.Id);
        Assert.Equal(5, reloadedAccount!.FailedLoginAttemptCount);
        Assert.Equal(8, reloadedAccount.LockoutCount);
        Assert.Equal(Now.AddHours(24), reloadedAccount.LockoutEndsAt);
    }

    // Deux requêtes lisent le même compteur : la seconde ne doit pas écraser l'essai compté par la première.
    [Fact]
    public async Task CountersAreNotSavedWhenAnotherRequestJustChangedThem()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var accountRepository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-compteurs-conflit@exemple.test");
        var account = await accountRepository.FindByIdAsync(savedAccount.Id);
        await SimulateConcurrentAttemptAsync(databaseContext, savedAccount.Id, 3);

        account!.CountLoginAttempt(Now);
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.False(saved);
        var reloadedAccount = await accountRepository.FindByIdAsync(savedAccount.Id);
        Assert.Equal(3, reloadedAccount!.FailedLoginAttemptCount);
    }

    // Une récupération vient de changer le mot de passe : la connexion en cours ne doit rien enregistrer,
    // même si ses compteurs n'ont pas changé. Sans ce contrôle, une session survivrait au changement de mot de passe.
    [Fact]
    public async Task LoginIsNotSavedWhenThePasswordJustChangedEvenWithUnchangedCounters()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-session-mot-de-passe-change@exemple.test");
        var account = await new UserAccountRepository(databaseContext).FindByIdAsync(savedAccount.Id);

        await WriteAsAnotherRequestAsync(databaseContext, () => databaseContext.Users
            .Where(row => row.Id == savedAccount.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                row => row.PasswordHash,
                "empreinte-du-nouveau-mot-de-passe")));

        PrepareLogin(
            databaseContext,
            account!,
            CreateSession(savedAccount, Now),
            null,
            CreateLoginEvent(savedAccount));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.False(saved);
        Assert.Equal(0, await databaseContext.UserSessions.CountAsync(row => row.UserId == savedAccount.Id));
        Assert.Equal(0, await databaseContext.SecurityEvents.CountAsync(row => row.UserId == savedAccount.Id));
    }

    // Une autre requête change une colonne que la connexion ne regarde pas, comme le nom : la connexion recommence quand même,
    // car la version de la ligne couvre toutes les colonnes. Une règle future sur n'importe quelle colonne est donc protégée.
    [Fact]
    public async Task LoginIsNotSavedWhenAnyColumnOfTheAccountJustChanged()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-session-nom-change@exemple.test");
        var account = await new UserAccountRepository(databaseContext).FindByIdAsync(savedAccount.Id);

        await WriteAsAnotherRequestAsync(databaseContext, () => databaseContext.Users
            .Where(row => row.Id == savedAccount.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                row => row.LastName,
                "Nom-d-une-autre-requete")));

        PrepareLogin(
            databaseContext,
            account!,
            CreateSession(savedAccount, Now),
            null,
            CreateLoginEvent(savedAccount));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.False(saved);
        Assert.Equal(0, await databaseContext.UserSessions.CountAsync(row => row.UserId == savedAccount.Id));
    }

    // À chaque blocage, le compteur revient à 5 : une requête qui a lu le blocage précédent ne doit pas effacer le nouveau.
    // Ce cas a été trouvé par une vraie course entre plusieurs connexions.
    [Fact]
    public async Task CountersAreNotSavedWhenANewLockoutHappenedWithTheSameCounter()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var accountRepository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-compteurs-nouveau-blocage@exemple.test");
        var firstLockoutEnd = Now;
        var secondLockoutEnd = Now.AddMinutes(30);
        await databaseContext.Users
            .Where(row => row.Id == savedAccount.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.FailedLoginAttemptCount, (short)5)
                .SetProperty(row => row.LockoutEndsAt, firstLockoutEnd));
        var account = await accountRepository.FindByIdAsync(savedAccount.Id);

        await WriteAsAnotherRequestAsync(databaseContext, () => databaseContext.Users
            .Where(row => row.Id == savedAccount.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.LockoutEndsAt, secondLockoutEnd)));

        account!.CountLoginAttempt(Now);
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.False(saved);
        var reloadedAccount = await accountRepository.FindByIdAsync(savedAccount.Id);
        Assert.Equal(secondLockoutEnd, reloadedAccount!.LockoutEndsAt);
        Assert.Equal(5, reloadedAccount.FailedLoginAttemptCount);
    }

    [Fact]
    public async Task DeviceTokenBelongsOnlyToItsOwnAccount()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserSessionRepository(databaseContext);
        var firstAccount = await SaveConfirmedAccountAsync(databaseContext, "test-appareil-premier@exemple.test");
        var secondAccount = await SaveConfirmedAccountAsync(databaseContext, "test-appareil-second@exemple.test");
        var deviceTokenHash = new string('c', 64);
        databaseContext.Devices.Add(new Device { DeviceToken = deviceTokenHash, UserId = firstAccount.Id });
        await databaseContext.SaveChangesAsync();

        Assert.True(await repository.DeviceBelongsToAccountAsync(deviceTokenHash, firstAccount.Id));
        Assert.False(await repository.DeviceBelongsToAccountAsync(deviceTokenHash, secondAccount.Id));
        Assert.False(await repository.DeviceBelongsToAccountAsync(new string('d', 64), firstAccount.Id));
    }

    [Fact]
    public async Task SessionIsFoundByItsHashAndUnknownHashFindsNothing()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserSessionRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-session-recherche@exemple.test");
        var savedSession = CreateSession(savedAccount, Now);
        await SaveSessionsDirectlyAsync(databaseContext, savedSession);

        var session = await repository.FindAsync(savedSession.Id);

        Assert.Equal(savedAccount.Id, session!.UserId);
        Assert.Null(await repository.FindAsync(new string('e', 64)));
    }

    [Fact]
    public async Task ActivityOfTheSessionIsReallyRecordedInTheDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserSessionRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-session-activite@exemple.test");
        var session = CreateSession(savedAccount, Now);
        var otherSession = CreateSession(savedAccount, Now);
        await SaveSessionsDirectlyAsync(databaseContext, session, otherSession);

        await repository.RecordActivityAsync(session.Id, Now.AddMinutes(10));

        var reloadedSession = await repository.FindAsync(session.Id);
        var reloadedOtherSession = await repository.FindAsync(otherSession.Id);
        Assert.Equal(Now.AddMinutes(10), reloadedSession!.LastActivityAt);
        Assert.Equal(Now, reloadedOtherSession!.LastActivityAt);
    }

    // Deux requêtes écrivent leur activité dans le désordre : la plus ancienne ne doit pas raccourcir la session.
    [Fact]
    public async Task OlderActivityNeverReplacesANewerOneInTheDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserSessionRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-session-activite-desordre@exemple.test");
        var session = CreateSession(savedAccount, Now);
        await SaveSessionsDirectlyAsync(databaseContext, session);

        await repository.RecordActivityAsync(session.Id, Now.AddMinutes(10));
        await repository.RecordActivityAsync(session.Id, Now.AddMinutes(5));

        var reloadedSession = await repository.FindAsync(session.Id);
        Assert.Equal(Now.AddMinutes(10), reloadedSession!.LastActivityAt);
    }

    // Se déconnecter sur un appareil ne déconnecte pas les autres.
    [Fact]
    public async Task DeletingOneSessionKeepsTheOtherSessionsOfTheAccount()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserSessionRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-session-suppression@exemple.test");
        var session = CreateSession(savedAccount, Now);
        var otherSession = CreateSession(savedAccount, Now);
        await SaveSessionsDirectlyAsync(databaseContext, session, otherSession);

        await repository.DeleteAsync(session.Id);

        Assert.Null(await repository.FindAsync(session.Id));
        Assert.NotNull(await repository.FindAsync(otherSession.Id));
    }

    [Fact]
    public async Task CleanupDeletesOnlySessionsInactiveSinceTheDate()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserSessionRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-session-effacement@exemple.test");
        var expiredSession = CreateSession(savedAccount, Now);
        var activeSession = CreateSession(savedAccount, Now.AddSeconds(1));
        await SaveSessionsDirectlyAsync(databaseContext, expiredSession, activeSession);

        var deletedCount = await repository.DeleteInactiveSinceAsync(Now);

        Assert.True(deletedCount >= 1);
        Assert.Null(await repository.FindAsync(expiredSession.Id));
        Assert.NotNull(await repository.FindAsync(activeSession.Id));
    }

    // SS §3 : après une récupération, toutes les sessions du compte sont fermées, et seulement les siennes.
    [Fact]
    public async Task DeletingEverySessionOfOneAccountKeepsTheSessionsOfOtherAccounts()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserSessionRepository(databaseContext);
        var disconnectedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-session-toutes-fermees@exemple.test");
        var account = await SaveConfirmedAccountAsync(databaseContext, "test-session-gardee@exemple.test");
        var firstSession = CreateSession(disconnectedAccount, Now);
        var secondSession = CreateSession(disconnectedAccount, Now);
        var otherSession = CreateSession(account, Now);
        await SaveSessionsDirectlyAsync(databaseContext, firstSession, secondSession, otherSession);

        await repository.DeleteAllForUserAsync(disconnectedAccount.Id);

        Assert.Null(await repository.FindAsync(firstSession.Id));
        Assert.Null(await repository.FindAsync(secondSession.Id));
        Assert.NotNull(await repository.FindAsync(otherSession.Id));
    }

    // Quand le compte disparaît, ses sessions et ses appareils partent avec lui (MPD) : la session ne vaut plus rien.
    [Fact]
    public async Task ErasingTheAccountErasesItsSessionsAndItsDevices()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserSessionRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-session-compte-efface@exemple.test");
        var savedSession = CreateSession(savedAccount, Now);
        databaseContext.Devices.Add(new Device { DeviceToken = new string('f', 64), UserId = savedAccount.Id });
        await SaveSessionsDirectlyAsync(databaseContext, savedSession);

        await databaseContext.Users
            .Where(row => row.Id == savedAccount.Id)
            .ExecuteDeleteAsync();

        Assert.Null(await repository.FindAsync(savedSession.Id));
        Assert.Equal(0, await databaseContext.Devices.CountAsync(row => row.UserId == savedAccount.Id));
    }

    // Le compteur ne peut pas dépasser 5 en base : c'est le dernier filet si le code se trompe (MPD).
    [Fact]
    public async Task DatabaseRefusesACounterAboveFiveFailedAttempts()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-compteur-limite@exemple.test");

        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => SimulateConcurrentAttemptAsync(databaseContext, savedAccount.Id, 6));
    }
}
