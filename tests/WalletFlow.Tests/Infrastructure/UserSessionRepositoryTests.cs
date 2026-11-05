using System.Net;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.LocalDatabaseForTheTests;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les sessions, les appareils et les compteurs de connexion dans la vraie base PostgreSQL locale,
// avec l'utilisateur de l'API. Chaque test travaille dans une transaction annulée à la fin : la base reste vide.
public class UserSessionRepositoryTests
{
    private static readonly DateTime CurrentDateUsedInTheseTests = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    // Enregistre un nouveau compte confirmé avec cette adresse, puis oublie les objets gardés en mémoire.
    private static async Task<User> SaveNewConfirmedAccountWithThisEmailAddressAsync(
        WalletFlowDatabaseContext databaseContextOfThisRepositoryTest,
        string emailAddressOfTheNewAccountToSave)
    {
        var newAccountToSaveInTheRealDatabase = new User
        {
            Email = emailAddressOfTheNewAccountToSave,
            PasswordHash = "empreinte-de-test",
            IsEmailConfirmed = true
        };
        databaseContextOfThisRepositoryTest.Users.Add(newAccountToSaveInTheRealDatabase);
        await databaseContextOfThisRepositoryTest.SaveChangesAsync();
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();

        return newAccountToSaveInTheRealDatabase;
    }

    // Crée une session sans l'enregistrer, avec une empreinte de la taille d'un vrai SHA-256.
    private static UserSession CreateNewSessionForThisUserAccount(User accountThatOwnsTheNewSession, DateTime dateOfTheLastActivityOfTheSession)
    {
        return new UserSession
        {
            Id = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + new string('0', 32),
            LastActivityAt = dateOfTheLastActivityOfTheSession,
            UserId = accountThatOwnsTheNewSession.Id
        };
    }

    // Crée l'événement d'une connexion réussie, sans l'enregistrer.
    private static SecurityEvent CreateSecurityEventOfTheSuccessfulLogin(User accountThatHasJustLoggedIn)
    {
        return new SecurityEvent
        {
            Type = SecurityEventType.Login,
            Result = SecurityEventResult.Success,
            OccurredAt = CurrentDateUsedInTheseTests,
            IpAddress = IPAddress.Parse("203.0.113.7"),
            UserId = accountThatHasJustLoggedIn.Id
        };
    }

    // Enregistre directement des sessions déjà créées, puis oublie les objets gardés en mémoire.
    private static async Task SaveTheseSessionsDirectlyInTheDatabaseAsync(WalletFlowDatabaseContext databaseContextOfThisRepositoryTest, params UserSession[] sessionsToSaveInTheRealDatabase)
    {
        databaseContextOfThisRepositoryTest.UserSessions.AddRange(sessionsToSaveInTheRealDatabase);
        await databaseContextOfThisRepositoryTest.SaveChangesAsync();
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();
    }

    // Une autre requête change le compteur directement en base : les objets gardés en mémoire ne le voient pas.
    private static async Task SimulateAnotherRequestCountingOneAttemptAsync(WalletFlowDatabaseContext databaseContextOfThisRepositoryTest, Guid identifierOfTheAccountChangedByTheRequest, short numberOfFailedAttemptsWrittenInDatabase)
    {
        await databaseContextOfThisRepositoryTest.Users
            .Where(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == identifierOfTheAccountChangedByTheRequest)
            .ExecuteUpdateAsync(setterOfTheColumnsOfTheUser => setterOfTheColumnsOfTheUser.SetProperty(
                userBeingUpdatedInTheDatabase => userBeingUpdatedInTheDatabase.FailedLoginAttemptCount,
                numberOfFailedAttemptsWrittenInDatabase));
    }

    [Fact]
    public async Task SuccessfulLoginIsReallySavedWithItsCountersSessionDeviceAndEvent()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserSessionRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-session-connexion@exemple.test");
        var accountReadByThisSuccessfulLogin = await databaseContextOfThisRepositoryTest.Users.SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);
        accountReadByThisSuccessfulLogin.FailedLoginAttemptCount = 0;
        accountReadByThisSuccessfulLogin.LockoutCount = 0;
        var newSessionOfTheUserAccount = CreateNewSessionForThisUserAccount(accountSavedInDatabaseByThisTest, CurrentDateUsedInTheseTests);
        var newDeviceOfTheUserAccount = new Device { DeviceToken = new string('a', 64), UserId = accountSavedInDatabaseByThisTest.Id };

        var loginWasSavedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToSaveSuccessfulLoginOfThisAccountAsync(
            accountReadByThisSuccessfulLogin, newSessionOfTheUserAccount, newDeviceOfTheUserAccount, null, CreateSecurityEventOfTheSuccessfulLogin(accountSavedInDatabaseByThisTest));

        Assert.True(loginWasSavedInTheDatabase);

        // On oublie les objets en mémoire : tout ce qui suit est vraiment relu dans la base.
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();
        var sessionReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.UserSessions.SingleAsync(sessionSearchedInTheDatabaseTable =>
            sessionSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id);
        Assert.Equal(newSessionOfTheUserAccount.Id, sessionReadBackFromTheRealDatabase.Id);
        Assert.Equal(CurrentDateUsedInTheseTests, sessionReadBackFromTheRealDatabase.LastActivityAt);

        var deviceReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.Devices.SingleAsync(deviceSearchedInTheDatabaseTable =>
            deviceSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id);
        Assert.Equal(new string('a', 64), deviceReadBackFromTheRealDatabase.DeviceToken);

        var eventReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.SecurityEvents.SingleAsync(eventSearchedInTheDatabaseTable =>
            eventSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id);
        Assert.Equal(SecurityEventType.Login, eventReadBackFromTheRealDatabase.Type);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), eventReadBackFromTheRealDatabase.IpAddress);
    }

    [Fact]
    public async Task SuccessfulLoginWithoutNewDeviceSavesNoDevice()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserSessionRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-session-sans-appareil@exemple.test");
        var accountReadByThisSuccessfulLogin = await new UserAccountRepository(databaseContextOfThisRepositoryTest).FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);

        await repositoryBeingTestedOnTheRealDatabase.TryToSaveSuccessfulLoginOfThisAccountAsync(
            accountReadByThisSuccessfulLogin!, CreateNewSessionForThisUserAccount(accountSavedInDatabaseByThisTest, CurrentDateUsedInTheseTests), null, null, CreateSecurityEventOfTheSuccessfulLogin(accountSavedInDatabaseByThisTest));

        Assert.Equal(0, await databaseContextOfThisRepositoryTest.Devices.CountAsync(deviceSearchedInTheDatabaseTable => deviceSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id));
    }

    // Tout ou rien : un conflit sur le compte n'enregistre ni la session, ni l'appareil, ni l'événement.
    // Le même enregistrement doit ensuite réussir, une fois le compte relu.
    [Fact]
    public async Task ConflictOnTheCountersSavesNothingOfTheLoginAndTheLoginCanBeSavedAfterwards()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserSessionRepository(databaseContextOfThisRepositoryTest);
        var accountRepositoryUsedByThisDatabaseTest = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-session-conflit@exemple.test");
        var accountReadJustBeforeTheConflict = await accountRepositoryUsedByThisDatabaseTest.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        await SimulateAnotherRequestCountingOneAttemptAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id, 1);
        var newSessionOfTheUserAccount = CreateNewSessionForThisUserAccount(accountSavedInDatabaseByThisTest, CurrentDateUsedInTheseTests);
        var newDeviceOfTheUserAccount = new Device { DeviceToken = new string('b', 64), UserId = accountSavedInDatabaseByThisTest.Id };
        var securityEventOfTheSuccessfulUserLogin = CreateSecurityEventOfTheSuccessfulLogin(accountSavedInDatabaseByThisTest);

        // Le compteur lu est 0, mais la base contient 1 : cette écriture doit être refusée.
        accountReadJustBeforeTheConflict!.LockoutCount = 2;
        var firstTryWasSavedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToSaveSuccessfulLoginOfThisAccountAsync(
            accountReadJustBeforeTheConflict, newSessionOfTheUserAccount, newDeviceOfTheUserAccount, null, securityEventOfTheSuccessfulUserLogin);

        Assert.False(firstTryWasSavedInTheDatabase);
        Assert.Equal(0, await databaseContextOfThisRepositoryTest.UserSessions.CountAsync(sessionSearchedInTheDatabaseTable => sessionSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id));
        Assert.Equal(0, await databaseContextOfThisRepositoryTest.Devices.CountAsync(deviceSearchedInTheDatabaseTable => deviceSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id));
        Assert.Equal(0, await databaseContextOfThisRepositoryTest.SecurityEvents.CountAsync(eventSearchedInTheDatabaseTable => eventSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id));

        var accountReadAgainAfterTheConflict = await accountRepositoryUsedByThisDatabaseTest.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        Assert.Equal(1, accountReadAgainAfterTheConflict!.FailedLoginAttemptCount);
        Assert.Equal(0, accountReadAgainAfterTheConflict.LockoutCount);

        AccountLockoutRules.ResetLoginCountersAfterSuccessfulLogin(accountReadAgainAfterTheConflict);
        var secondTryWasSavedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToSaveSuccessfulLoginOfThisAccountAsync(
            accountReadAgainAfterTheConflict, newSessionOfTheUserAccount, newDeviceOfTheUserAccount, null, securityEventOfTheSuccessfulUserLogin);

        Assert.True(secondTryWasSavedInTheDatabase);
        Assert.Equal(1, await databaseContextOfThisRepositoryTest.UserSessions.CountAsync(sessionSearchedInTheDatabaseTable => sessionSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id));
    }

    [Fact]
    public async Task LoginCountersAreReallySavedInTheDatabase()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var accountRepositoryUsedByThisDatabaseTest = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-compteurs-enregistres@exemple.test");
        var accountReadByThisLoginAttempt = await accountRepositoryUsedByThisDatabaseTest.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        accountReadByThisLoginAttempt!.FailedLoginAttemptCount = 5;
        accountReadByThisLoginAttempt.LockoutCount = 8;
        accountReadByThisLoginAttempt.LockoutEndsAt = CurrentDateUsedInTheseTests.AddHours(24);

        var countersWereSavedInTheDatabase = await accountRepositoryUsedByThisDatabaseTest.TryToSaveLoginCountersOfThisAccountAsync(accountReadByThisLoginAttempt);

        Assert.True(countersWereSavedInTheDatabase);
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();
        var accountReadBackFromTheRealDatabase = await accountRepositoryUsedByThisDatabaseTest.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        Assert.Equal(5, accountReadBackFromTheRealDatabase!.FailedLoginAttemptCount);
        Assert.Equal(8, accountReadBackFromTheRealDatabase.LockoutCount);
        Assert.Equal(CurrentDateUsedInTheseTests.AddHours(24), accountReadBackFromTheRealDatabase.LockoutEndsAt);
    }

    // Deux requêtes lisent le même compteur : la seconde ne doit pas écraser l'essai compté par la première.
    [Fact]
    public async Task CountersAreNotSavedWhenAnotherRequestJustChangedThem()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var accountRepositoryUsedByThisDatabaseTest = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-compteurs-conflit@exemple.test");
        var accountReadJustBeforeTheConflict = await accountRepositoryUsedByThisDatabaseTest.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        await SimulateAnotherRequestCountingOneAttemptAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id, 3);

        accountReadJustBeforeTheConflict!.FailedLoginAttemptCount = 1;
        var countersWereSavedInTheDatabase = await accountRepositoryUsedByThisDatabaseTest.TryToSaveLoginCountersOfThisAccountAsync(accountReadJustBeforeTheConflict);

        Assert.False(countersWereSavedInTheDatabase);
        var accountReadAgainAfterTheConflict = await accountRepositoryUsedByThisDatabaseTest.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        Assert.Equal(3, accountReadAgainAfterTheConflict!.FailedLoginAttemptCount);
    }

    // Une récupération vient de changer le mot de passe : la connexion en cours ne doit rien enregistrer,
    // même si ses compteurs n'ont pas changé. Sans ce contrôle, une session survivrait au changement de mot de passe.
    [Fact]
    public async Task LoginIsNotSavedWhenThePasswordJustChangedEvenWithUnchangedCounters()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserSessionRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-session-mot-de-passe-change@exemple.test");
        var accountReadByThisSuccessfulLogin = await new UserAccountRepository(databaseContextOfThisRepositoryTest).FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);

        await databaseContextOfThisRepositoryTest.Users
            .Where(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id)
            .ExecuteUpdateAsync(setterOfTheColumnsOfTheUser => setterOfTheColumnsOfTheUser.SetProperty(
                userBeingUpdatedInTheDatabase => userBeingUpdatedInTheDatabase.PasswordHash,
                "empreinte-du-nouveau-mot-de-passe"));

        var loginWasSavedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToSaveSuccessfulLoginOfThisAccountAsync(
            accountReadByThisSuccessfulLogin!,
            CreateNewSessionForThisUserAccount(accountSavedInDatabaseByThisTest, CurrentDateUsedInTheseTests),
            null,
            null,
            CreateSecurityEventOfTheSuccessfulLogin(accountSavedInDatabaseByThisTest));

        Assert.False(loginWasSavedInTheDatabase);
        Assert.Equal(0, await databaseContextOfThisRepositoryTest.UserSessions.CountAsync(sessionSearchedInTheDatabaseTable => sessionSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id));
        Assert.Equal(0, await databaseContextOfThisRepositoryTest.SecurityEvents.CountAsync(eventSearchedInTheDatabaseTable => eventSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id));
    }

    // À chaque blocage, le compteur revient à 5 : une requête qui a lu le blocage précédent ne doit pas effacer le nouveau.
    // Ce cas a été trouvé par une vraie course entre plusieurs connexions.
    [Fact]
    public async Task CountersAreNotSavedWhenANewLockoutHappenedWithTheSameCounter()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var accountRepositoryUsedByThisDatabaseTest = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-compteurs-nouveau-blocage@exemple.test");
        var endOfTheFirstLockoutInThisTest = CurrentDateUsedInTheseTests;
        var endOfTheSecondLockoutInThisTest = CurrentDateUsedInTheseTests.AddMinutes(30);
        await databaseContextOfThisRepositoryTest.Users
            .Where(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id)
            .ExecuteUpdateAsync(setterOfTheColumnsOfTheUser => setterOfTheColumnsOfTheUser
                .SetProperty(userBeingUpdatedInTheDatabase => userBeingUpdatedInTheDatabase.FailedLoginAttemptCount, (short)5)
                .SetProperty(userBeingUpdatedInTheDatabase => userBeingUpdatedInTheDatabase.LockoutEndsAt, endOfTheFirstLockoutInThisTest));
        var accountReadWithTheFirstLockout = await accountRepositoryUsedByThisDatabaseTest.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);

        await databaseContextOfThisRepositoryTest.Users
            .Where(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id)
            .ExecuteUpdateAsync(setterOfTheColumnsOfTheUser => setterOfTheColumnsOfTheUser
                .SetProperty(userBeingUpdatedInTheDatabase => userBeingUpdatedInTheDatabase.LockoutEndsAt, endOfTheSecondLockoutInThisTest));

        AccountLockoutRules.CountOneLoginAttemptAndLockAccountOnTheLastOne(accountReadWithTheFirstLockout!, CurrentDateUsedInTheseTests);
        var countersWereSavedInTheDatabase = await accountRepositoryUsedByThisDatabaseTest.TryToSaveLoginCountersOfThisAccountAsync(accountReadWithTheFirstLockout!);

        Assert.False(countersWereSavedInTheDatabase);
        var accountReadAgainAfterTheConflict = await accountRepositoryUsedByThisDatabaseTest.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        Assert.Equal(endOfTheSecondLockoutInThisTest, accountReadAgainAfterTheConflict!.LockoutEndsAt);
        Assert.Equal(5, accountReadAgainAfterTheConflict.FailedLoginAttemptCount);
    }

    [Fact]
    public async Task DeviceTokenBelongsOnlyToItsOwnAccount()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserSessionRepository(databaseContextOfThisRepositoryTest);
        var firstAccountSavedByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-appareil-premier@exemple.test");
        var secondAccountSavedByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-appareil-second@exemple.test");
        var hashOfTheDeviceTokenSavedByThisTest = new string('c', 64);
        databaseContextOfThisRepositoryTest.Devices.Add(new Device { DeviceToken = hashOfTheDeviceTokenSavedByThisTest, UserId = firstAccountSavedByThisTest.Id });
        await databaseContextOfThisRepositoryTest.SaveChangesAsync();

        Assert.True(await repositoryBeingTestedOnTheRealDatabase.CheckThatDeviceTokenBelongsToThisAccountAsync(hashOfTheDeviceTokenSavedByThisTest, firstAccountSavedByThisTest.Id));
        Assert.False(await repositoryBeingTestedOnTheRealDatabase.CheckThatDeviceTokenBelongsToThisAccountAsync(hashOfTheDeviceTokenSavedByThisTest, secondAccountSavedByThisTest.Id));
        Assert.False(await repositoryBeingTestedOnTheRealDatabase.CheckThatDeviceTokenBelongsToThisAccountAsync(new string('d', 64), firstAccountSavedByThisTest.Id));
    }

    [Fact]
    public async Task SessionIsFoundByItsHashAndUnknownHashFindsNothing()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserSessionRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-session-recherche@exemple.test");
        var sessionSavedInDatabaseByThisTest = CreateNewSessionForThisUserAccount(accountSavedInDatabaseByThisTest, CurrentDateUsedInTheseTests);
        await SaveTheseSessionsDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, sessionSavedInDatabaseByThisTest);

        var sessionFoundInTheRealDatabase = await repositoryBeingTestedOnTheRealDatabase.FindUserSessionByItsHashAsync(sessionSavedInDatabaseByThisTest.Id);

        Assert.Equal(accountSavedInDatabaseByThisTest.Id, sessionFoundInTheRealDatabase!.UserId);
        Assert.Null(await repositoryBeingTestedOnTheRealDatabase.FindUserSessionByItsHashAsync(new string('e', 64)));
    }

    [Fact]
    public async Task ActivityOfTheSessionIsReallyRecordedInTheDatabase()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserSessionRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-session-activite@exemple.test");
        var sessionUsedByThisHttpRequest = CreateNewSessionForThisUserAccount(accountSavedInDatabaseByThisTest, CurrentDateUsedInTheseTests);
        var sessionOfAnotherBrowserOfTheUser = CreateNewSessionForThisUserAccount(accountSavedInDatabaseByThisTest, CurrentDateUsedInTheseTests);
        await SaveTheseSessionsDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, sessionUsedByThisHttpRequest, sessionOfAnotherBrowserOfTheUser);

        await repositoryBeingTestedOnTheRealDatabase.RecordActivityOfThisUserSessionAsync(sessionUsedByThisHttpRequest.Id, CurrentDateUsedInTheseTests.AddMinutes(10));

        var sessionUsedAndReadBackFromDatabase = await repositoryBeingTestedOnTheRealDatabase.FindUserSessionByItsHashAsync(sessionUsedByThisHttpRequest.Id);
        var sessionOfAnotherBrowserReadBack = await repositoryBeingTestedOnTheRealDatabase.FindUserSessionByItsHashAsync(sessionOfAnotherBrowserOfTheUser.Id);
        Assert.Equal(CurrentDateUsedInTheseTests.AddMinutes(10), sessionUsedAndReadBackFromDatabase!.LastActivityAt);
        Assert.Equal(CurrentDateUsedInTheseTests, sessionOfAnotherBrowserReadBack!.LastActivityAt);
    }

    // Se déconnecter sur un appareil ne déconnecte pas les autres.
    [Fact]
    public async Task DeletingOneSessionKeepsTheOtherSessionsOfTheAccount()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserSessionRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-session-suppression@exemple.test");
        var sessionToDeleteByThisTest = CreateNewSessionForThisUserAccount(accountSavedInDatabaseByThisTest, CurrentDateUsedInTheseTests);
        var sessionOfAnotherBrowserOfTheUser = CreateNewSessionForThisUserAccount(accountSavedInDatabaseByThisTest, CurrentDateUsedInTheseTests);
        await SaveTheseSessionsDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, sessionToDeleteByThisTest, sessionOfAnotherBrowserOfTheUser);

        await repositoryBeingTestedOnTheRealDatabase.DeleteUserSessionByItsHashAsync(sessionToDeleteByThisTest.Id);

        Assert.Null(await repositoryBeingTestedOnTheRealDatabase.FindUserSessionByItsHashAsync(sessionToDeleteByThisTest.Id));
        Assert.NotNull(await repositoryBeingTestedOnTheRealDatabase.FindUserSessionByItsHashAsync(sessionOfAnotherBrowserOfTheUser.Id));
    }

    [Fact]
    public async Task CleanupDeletesOnlySessionsInactiveSinceTheDate()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserSessionRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-session-effacement@exemple.test");
        var sessionInactiveExactlySinceTheDate = CreateNewSessionForThisUserAccount(accountSavedInDatabaseByThisTest, CurrentDateUsedInTheseTests);
        var sessionActiveOneSecondAfterTheDate = CreateNewSessionForThisUserAccount(accountSavedInDatabaseByThisTest, CurrentDateUsedInTheseTests.AddSeconds(1));
        await SaveTheseSessionsDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, sessionInactiveExactlySinceTheDate, sessionActiveOneSecondAfterTheDate);

        var numberOfSessionsDeletedByThisCleanup = await repositoryBeingTestedOnTheRealDatabase.DeleteUserSessionsInactiveSinceThisDateAsync(CurrentDateUsedInTheseTests);

        Assert.True(numberOfSessionsDeletedByThisCleanup >= 1);
        Assert.Null(await repositoryBeingTestedOnTheRealDatabase.FindUserSessionByItsHashAsync(sessionInactiveExactlySinceTheDate.Id));
        Assert.NotNull(await repositoryBeingTestedOnTheRealDatabase.FindUserSessionByItsHashAsync(sessionActiveOneSecondAfterTheDate.Id));
    }

    // SS §3 : après une récupération, toutes les sessions du compte sont fermées, et seulement les siennes.
    [Fact]
    public async Task DeletingEverySessionOfOneAccountKeepsTheSessionsOfOtherAccounts()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserSessionRepository(databaseContextOfThisRepositoryTest);
        var accountDisconnectedEverywhereByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-session-toutes-fermees@exemple.test");
        var accountThatKeepsItsSessionInThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-session-gardee@exemple.test");
        var firstSessionOfTheDisconnectedAccount = CreateNewSessionForThisUserAccount(accountDisconnectedEverywhereByThisTest, CurrentDateUsedInTheseTests);
        var secondSessionOfTheDisconnectedAccount = CreateNewSessionForThisUserAccount(accountDisconnectedEverywhereByThisTest, CurrentDateUsedInTheseTests);
        var sessionOfTheOtherAccountInThisTest = CreateNewSessionForThisUserAccount(accountThatKeepsItsSessionInThisTest, CurrentDateUsedInTheseTests);
        await SaveTheseSessionsDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, firstSessionOfTheDisconnectedAccount, secondSessionOfTheDisconnectedAccount, sessionOfTheOtherAccountInThisTest);

        await repositoryBeingTestedOnTheRealDatabase.DeleteAllUserSessionsOfThisAccountAsync(accountDisconnectedEverywhereByThisTest.Id);

        Assert.Null(await repositoryBeingTestedOnTheRealDatabase.FindUserSessionByItsHashAsync(firstSessionOfTheDisconnectedAccount.Id));
        Assert.Null(await repositoryBeingTestedOnTheRealDatabase.FindUserSessionByItsHashAsync(secondSessionOfTheDisconnectedAccount.Id));
        Assert.NotNull(await repositoryBeingTestedOnTheRealDatabase.FindUserSessionByItsHashAsync(sessionOfTheOtherAccountInThisTest.Id));
    }

    // Quand le compte disparaît, ses sessions et ses appareils partent avec lui (MPD) : la session ne vaut plus rien.
    [Fact]
    public async Task ErasingTheAccountErasesItsSessionsAndItsDevices()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserSessionRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-session-compte-efface@exemple.test");
        var sessionSavedInDatabaseByThisTest = CreateNewSessionForThisUserAccount(accountSavedInDatabaseByThisTest, CurrentDateUsedInTheseTests);
        databaseContextOfThisRepositoryTest.Devices.Add(new Device { DeviceToken = new string('f', 64), UserId = accountSavedInDatabaseByThisTest.Id });
        await SaveTheseSessionsDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, sessionSavedInDatabaseByThisTest);

        await databaseContextOfThisRepositoryTest.Users
            .Where(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id)
            .ExecuteDeleteAsync();

        Assert.Null(await repositoryBeingTestedOnTheRealDatabase.FindUserSessionByItsHashAsync(sessionSavedInDatabaseByThisTest.Id));
        Assert.Equal(0, await databaseContextOfThisRepositoryTest.Devices.CountAsync(deviceSearchedInTheDatabaseTable => deviceSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id));
    }

    // Le compteur ne peut pas dépasser 5 en base : c'est le dernier filet si le code se trompe (MPD).
    [Fact]
    public async Task DatabaseRefusesACounterAboveFiveFailedAttempts()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-compteur-limite@exemple.test");

        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => SimulateAnotherRequestCountingOneAttemptAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id, 6));
    }
}
