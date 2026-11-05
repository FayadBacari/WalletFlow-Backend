using System.Net;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.LocalDatabaseForTheTests;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la double authentification dans la vraie base PostgreSQL locale, avec l'utilisateur de l'API.
// Chaque test travaille dans une transaction annulée à la fin : la base reste vide.
public class TwoFactorAuthenticationRepositoryTests
{
    private const string SecretOfThePhoneEncryptedInTheseTests = "secret-chiffre-de-ces-tests";

    private static readonly DateTime CurrentDateUsedInTheseTests = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    // Enregistre un nouveau compte confirmé avec cette adresse, puis oublie les objets gardés en mémoire.
    private static async Task<User> SaveNewConfirmedAccountWithThisEmailAddressAsync(WalletFlowDatabaseContext databaseContextOfThisRepositoryTest, string emailAddressOfTheNewAccountToSave)
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

    // Crée ce nombre de codes de secours, sans les enregistrer.
    private static List<BackupCode> CreateBackupCodesForThisAccount(Guid identifierOfTheAccountOwningTheCodes, int numberOfBackupCodesToCreate)
    {
        var newBackupCodesOfTheAccount = new List<BackupCode>();

        for (var numberOfTheBackupCodeBeingCreated = 1; numberOfTheBackupCodeBeingCreated <= numberOfBackupCodesToCreate; numberOfTheBackupCodeBeingCreated++)
        {
            newBackupCodesOfTheAccount.Add(new BackupCode { CodeHash = "empreinte-du-code-" + numberOfTheBackupCodeBeingCreated, UserId = identifierOfTheAccountOwningTheCodes });
        }

        return newBackupCodesOfTheAccount;
    }

    // Crée un événement du journal, sans l'enregistrer.
    private static SecurityEvent CreateSecurityEventForThisAccount(Guid identifierOfTheAccountInTheJournal, SecurityEventType typeOfTheEventToCreate)
    {
        return new SecurityEvent
        {
            Type = typeOfTheEventToCreate,
            Result = SecurityEventResult.Success,
            OccurredAt = CurrentDateUsedInTheseTests,
            IpAddress = IPAddress.Parse("203.0.113.7"),
            UserId = identifierOfTheAccountInTheJournal
        };
    }

    // Active un compte directement, comme le ferait une activation déjà réussie, puis oublie les objets gardés en mémoire.
    private static async Task ActivateThisAccountDirectlyInTheDatabaseAsync(WalletFlowDatabaseContext databaseContextOfThisRepositoryTest, Guid identifierOfTheAccountToActivate, int numberOfBackupCodesToCreate)
    {
        var accountReadJustBeforeTheActivation = await databaseContextOfThisRepositoryTest.Users.SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == identifierOfTheAccountToActivate);
        await new TwoFactorAuthenticationRepository(databaseContextOfThisRepositoryTest).TryToSaveTwoFactorActivationOfThisAccountAsync(
            accountReadJustBeforeTheActivation,
            SecretOfThePhoneEncryptedInTheseTests,
            CreateBackupCodesForThisAccount(identifierOfTheAccountToActivate, numberOfBackupCodesToCreate),
            CreateSecurityEventForThisAccount(identifierOfTheAccountToActivate, SecurityEventType.TwoFactorActivation));
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();
    }

    // Une autre requête change le compteur directement en base : les objets gardés en mémoire ne le voient pas.
    private static async Task SimulateAnotherRequestCountingOneAttemptAsync(WalletFlowDatabaseContext databaseContextOfThisRepositoryTest, Guid identifierOfTheAccountChangedByTheRequest)
    {
        await databaseContextOfThisRepositoryTest.Users
            .Where(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == identifierOfTheAccountChangedByTheRequest)
            .ExecuteUpdateAsync(setterOfTheColumnsOfTheUser => setterOfTheColumnsOfTheUser.SetProperty(
                userBeingUpdatedInTheDatabase => userBeingUpdatedInTheDatabase.FailedLoginAttemptCount,
                (short)1));
    }

    // Une autre requête enregistre son activation directement en base : les objets gardés en mémoire ne le voient pas.
    private static async Task SimulateAnotherRequestActivatingTheAccountAsync(WalletFlowDatabaseContext databaseContextOfThisRepositoryTest, Guid identifierOfTheAccountChangedByTheRequest)
    {
        await databaseContextOfThisRepositoryTest.Users
            .Where(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == identifierOfTheAccountChangedByTheRequest)
            .ExecuteUpdateAsync(setterOfTheColumnsOfTheUser => setterOfTheColumnsOfTheUser
                .SetProperty(userBeingUpdatedInTheDatabase => userBeingUpdatedInTheDatabase.IsTwoFactorEnabled, true)
                .SetProperty(userBeingUpdatedInTheDatabase => userBeingUpdatedInTheDatabase.EncryptedTotpSecret, "secret-de-l-autre-requete"));
    }

    // Compte les événements de ce type dans le vrai journal, pour ce compte.
    private static async Task<int> CountSecurityEventsOfThisAccountAsync(WalletFlowDatabaseContext databaseContextOfThisRepositoryTest, Guid identifierOfTheAccountInTheJournal, SecurityEventType typeOfTheEventsToCount)
    {
        return await databaseContextOfThisRepositoryTest.SecurityEvents.CountAsync(eventSearchedInTheDatabaseTable =>
            eventSearchedInTheDatabaseTable.UserId == identifierOfTheAccountInTheJournal
            && eventSearchedInTheDatabaseTable.Type == typeOfTheEventsToCount);
    }

    // Vérifie que la base n'a rien gardé de l'activation pour ce compte, à part ce qu'un test y a écrit lui-même.
    private static async Task<int> CountBackupCodesOfThisAccountAsync(WalletFlowDatabaseContext databaseContextOfThisRepositoryTest, Guid identifierOfTheAccountOwningTheCodes)
    {
        return await databaseContextOfThisRepositoryTest.BackupCodes.CountAsync(backupCodeSearchedInTheDatabaseTable => backupCodeSearchedInTheDatabaseTable.UserId == identifierOfTheAccountOwningTheCodes);
    }

    // --- L'activation

    // La contrainte ck_utilisateur_totp du MPD refuserait un compte activé sans secret : ce test passe par le vrai script.
    [Fact]
    public async Task ActivationIsReallySavedWithItsSecretItsCodesAndItsEvent()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new TwoFactorAuthenticationRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-activation-reussie@exemple.test");
        var accountReadJustBeforeTheActivation = await databaseContextOfThisRepositoryTest.Users.SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);

        var activationWasSavedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToSaveTwoFactorActivationOfThisAccountAsync(
            accountReadJustBeforeTheActivation,
            SecretOfThePhoneEncryptedInTheseTests,
            CreateBackupCodesForThisAccount(accountSavedInDatabaseByThisTest.Id, 10),
            CreateSecurityEventForThisAccount(accountSavedInDatabaseByThisTest.Id, SecurityEventType.TwoFactorActivation));

        Assert.True(activationWasSavedInTheDatabase);

        // On oublie les objets en mémoire : tout ce qui suit est vraiment relu dans la base.
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();
        var accountReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.Users.AsNoTracking().SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);
        Assert.True(accountReadBackFromTheRealDatabase.IsTwoFactorEnabled);
        Assert.Equal(SecretOfThePhoneEncryptedInTheseTests, accountReadBackFromTheRealDatabase.EncryptedTotpSecret);
        Assert.Equal(10, await CountBackupCodesOfThisAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id));
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id, SecurityEventType.TwoFactorActivation));
    }

    // Tout ou rien : un conflit n'écrit ni le secret, ni les codes, ni l'événement. Le même enregistrement réussit ensuite, une fois le compte relu.
    [Fact]
    public async Task ConflictOnTheAccountSavesNothingOfTheActivationAndItCanBeSavedAfterwards()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new TwoFactorAuthenticationRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-activation-conflit@exemple.test");
        var accountReadJustBeforeTheConflict = await databaseContextOfThisRepositoryTest.Users.SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);
        await SimulateAnotherRequestCountingOneAttemptAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id);
        var backupCodesCreatedForThisTwoFactorActivation = CreateBackupCodesForThisAccount(accountSavedInDatabaseByThisTest.Id, 10);
        var securityEventOfThisTwoFactorActivation = CreateSecurityEventForThisAccount(accountSavedInDatabaseByThisTest.Id, SecurityEventType.TwoFactorActivation);

        var firstTryWasSavedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToSaveTwoFactorActivationOfThisAccountAsync(accountReadJustBeforeTheConflict, SecretOfThePhoneEncryptedInTheseTests, backupCodesCreatedForThisTwoFactorActivation, securityEventOfThisTwoFactorActivation);

        Assert.False(firstTryWasSavedInTheDatabase);
        Assert.Equal(0, await CountBackupCodesOfThisAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id));
        Assert.Equal(0, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id, SecurityEventType.TwoFactorActivation));
        var accountReadAgainAfterTheConflict = await databaseContextOfThisRepositoryTest.Users.SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);
        Assert.False(accountReadAgainAfterTheConflict.IsTwoFactorEnabled);
        Assert.Null(accountReadAgainAfterTheConflict.EncryptedTotpSecret);

        var secondTryWasSavedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToSaveTwoFactorActivationOfThisAccountAsync(accountReadAgainAfterTheConflict, SecretOfThePhoneEncryptedInTheseTests, backupCodesCreatedForThisTwoFactorActivation, securityEventOfThisTwoFactorActivation);

        Assert.True(secondTryWasSavedInTheDatabase);
        Assert.Equal(10, await CountBackupCodesOfThisAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id));
    }

    // Deux activations simultanées : la seconde ne remplace pas le secret de la première, et le compte n'a jamais 20 codes.
    [Fact]
    public async Task SecondActivationAtTheSameMomentSavesNothing()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new TwoFactorAuthenticationRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-activation-double@exemple.test");
        var accountReadJustBeforeTheOtherActivation = await databaseContextOfThisRepositoryTest.Users.SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);
        await SimulateAnotherRequestActivatingTheAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id);

        var activationWasSavedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToSaveTwoFactorActivationOfThisAccountAsync(
            accountReadJustBeforeTheOtherActivation,
            SecretOfThePhoneEncryptedInTheseTests,
            CreateBackupCodesForThisAccount(accountSavedInDatabaseByThisTest.Id, 10),
            CreateSecurityEventForThisAccount(accountSavedInDatabaseByThisTest.Id, SecurityEventType.TwoFactorActivation));

        Assert.False(activationWasSavedInTheDatabase);
        Assert.Equal(0, await CountBackupCodesOfThisAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id));
        var accountReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.Users.AsNoTracking().SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);
        Assert.Equal("secret-de-l-autre-requete", accountReadBackFromTheRealDatabase.EncryptedTotpSecret);
    }

    // --- La lecture des codes

    [Fact]
    public async Task OnlyUnusedBackupCodesOfThisAccountAreFound()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new TwoFactorAuthenticationRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-codes-inutilises@exemple.test");
        var otherAccountSavedByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-codes-autre-compte@exemple.test");
        await ActivateThisAccountDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id, 3);
        await ActivateThisAccountDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, otherAccountSavedByThisTest.Id, 2);
        var backupCodeAlreadyUsedBeforeThisTest = await databaseContextOfThisRepositoryTest.BackupCodes.FirstAsync(backupCodeSearchedInTheDatabaseTable => backupCodeSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id);
        backupCodeAlreadyUsedBeforeThisTest.UsedAt = CurrentDateUsedInTheseTests;
        await databaseContextOfThisRepositoryTest.SaveChangesAsync();
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();

        var unusedBackupCodesFoundInTheRealDatabase = await repositoryBeingTestedOnTheRealDatabase.FindUnusedBackupCodesOfThisAccountAsync(accountSavedInDatabaseByThisTest.Id);

        Assert.Equal(2, unusedBackupCodesFoundInTheRealDatabase.Count);
        foreach (var unusedBackupCodeFoundInTheRealDatabase in unusedBackupCodesFoundInTheRealDatabase)
        {
            Assert.Equal(accountSavedInDatabaseByThisTest.Id, unusedBackupCodeFoundInTheRealDatabase.UserId);
            Assert.NotEqual(backupCodeAlreadyUsedBeforeThisTest.Id, unusedBackupCodeFoundInTheRealDatabase.Id);
            Assert.Null(unusedBackupCodeFoundInTheRealDatabase.UsedAt);
        }

        // Cette lecture ne sert qu'à comparer : EF Core ne doit pas suivre ces lignes.
        Assert.Empty(databaseContextOfThisRepositoryTest.ChangeTracker.Entries<BackupCode>());
    }

    [Fact]
    public async Task BackupCodeIsFoundByItsIdentifierAndUnknownIdentifierFindsNothing()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new TwoFactorAuthenticationRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-code-par-identifiant@exemple.test");
        await ActivateThisAccountDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id, 1);
        var backupCodeSavedForThisUserAccount = await databaseContextOfThisRepositoryTest.BackupCodes.AsNoTracking().SingleAsync(backupCodeSearchedInTheDatabaseTable => backupCodeSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id);

        Assert.Equal(backupCodeSavedForThisUserAccount.CodeHash, (await repositoryBeingTestedOnTheRealDatabase.FindBackupCodeByItsIdentifierAsync(backupCodeSavedForThisUserAccount.Id))!.CodeHash);
        Assert.Null(await repositoryBeingTestedOnTheRealDatabase.FindBackupCodeByItsIdentifierAsync(Guid.NewGuid()));
    }

    // --- La connexion avec un code de secours

    [Fact]
    public async Task LoginWithABackupCodeReallyMarksItAsUsedAtTheDateOfTheLogin()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-connexion-code-secours@exemple.test");
        await ActivateThisAccountDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id, 2);
        var accountReadJustBeforeTheLogin = await databaseContextOfThisRepositoryTest.Users.SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);
        var backupCodeUsedForTheLogin = await databaseContextOfThisRepositoryTest.BackupCodes.FirstAsync(backupCodeSearchedInTheDatabaseTable => backupCodeSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id);

        var loginWasSavedInTheDatabase = await new UserSessionRepository(databaseContextOfThisRepositoryTest).TryToSaveSuccessfulLoginOfThisAccountAsync(
            accountReadJustBeforeTheLogin,
            new UserSession { Id = new string('c', 64), LastActivityAt = CurrentDateUsedInTheseTests, UserId = accountSavedInDatabaseByThisTest.Id },
            null,
            backupCodeUsedForTheLogin,
            CreateSecurityEventForThisAccount(accountSavedInDatabaseByThisTest.Id, SecurityEventType.Login));

        Assert.True(loginWasSavedInTheDatabase);
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();
        var backupCodeReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.BackupCodes.AsNoTracking().SingleAsync(backupCodeSearchedInTheDatabaseTable => backupCodeSearchedInTheDatabaseTable.Id == backupCodeUsedForTheLogin.Id);
        Assert.Equal(CurrentDateUsedInTheseTests, backupCodeReadBackFromTheRealDatabase.UsedAt);
        Assert.Single(await new TwoFactorAuthenticationRepository(databaseContextOfThisRepositoryTest).FindUnusedBackupCodesOfThisAccountAsync(accountSavedInDatabaseByThisTest.Id));
    }

    // Deux requêtes avec le même code : celle qui écrit en second n'ouvre aucune session.
    [Fact]
    public async Task BackupCodeUsedByAnotherRequestJustBeforeTheSaveOpensNoSession()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-code-secours-course@exemple.test");
        await ActivateThisAccountDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id, 1);
        var accountReadJustBeforeTheLogin = await databaseContextOfThisRepositoryTest.Users.SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);
        var backupCodeUsedForTheLogin = await databaseContextOfThisRepositoryTest.BackupCodes.SingleAsync(backupCodeSearchedInTheDatabaseTable => backupCodeSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id);

        await databaseContextOfThisRepositoryTest.BackupCodes
            .Where(backupCodeSearchedInTheDatabaseTable => backupCodeSearchedInTheDatabaseTable.Id == backupCodeUsedForTheLogin.Id)
            .ExecuteUpdateAsync(setterOfTheColumnsOfTheCode => setterOfTheColumnsOfTheCode.SetProperty(
                backupCodeBeingUpdatedInTheDatabase => backupCodeBeingUpdatedInTheDatabase.UsedAt,
                CurrentDateUsedInTheseTests));

        var loginWasSavedInTheDatabase = await new UserSessionRepository(databaseContextOfThisRepositoryTest).TryToSaveSuccessfulLoginOfThisAccountAsync(
            accountReadJustBeforeTheLogin,
            new UserSession { Id = new string('d', 64), LastActivityAt = CurrentDateUsedInTheseTests.AddSeconds(1), UserId = accountSavedInDatabaseByThisTest.Id },
            null,
            backupCodeUsedForTheLogin,
            CreateSecurityEventForThisAccount(accountSavedInDatabaseByThisTest.Id, SecurityEventType.Login));

        Assert.False(loginWasSavedInTheDatabase);
        Assert.Equal(0, await databaseContextOfThisRepositoryTest.UserSessions.CountAsync(sessionSearchedInTheDatabaseTable => sessionSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id));
    }

    // Une connexion lancée sans code ne peut pas s'enregistrer si la double authentification vient d'être activée.
    [Fact]
    public async Task LoginReadBeforeAnActivationOpensNoSession()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-connexion-avant-activation@exemple.test");
        var accountReadJustBeforeTheActivation = await databaseContextOfThisRepositoryTest.Users.SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);
        await SimulateAnotherRequestActivatingTheAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id);

        var loginWasSavedInTheDatabase = await new UserSessionRepository(databaseContextOfThisRepositoryTest).TryToSaveSuccessfulLoginOfThisAccountAsync(
            accountReadJustBeforeTheActivation,
            new UserSession { Id = new string('e', 64), LastActivityAt = CurrentDateUsedInTheseTests, UserId = accountSavedInDatabaseByThisTest.Id },
            null,
            null,
            CreateSecurityEventForThisAccount(accountSavedInDatabaseByThisTest.Id, SecurityEventType.Login));

        Assert.False(loginWasSavedInTheDatabase);
        Assert.Equal(0, await databaseContextOfThisRepositoryTest.UserSessions.CountAsync(sessionSearchedInTheDatabaseTable => sessionSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id));
    }

    // --- La désactivation

    [Fact]
    public async Task DeactivationReallyErasesTheSecretAndEveryBackupCodeOfThisAccountOnly()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new TwoFactorAuthenticationRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-desactivation@exemple.test");
        var otherAccountSavedByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-desactivation-autre@exemple.test");
        await ActivateThisAccountDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id, 10);
        await ActivateThisAccountDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, otherAccountSavedByThisTest.Id, 10);
        var backupCodeAlreadyUsedBeforeThisTest = await databaseContextOfThisRepositoryTest.BackupCodes.FirstAsync(backupCodeSearchedInTheDatabaseTable => backupCodeSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id);
        backupCodeAlreadyUsedBeforeThisTest.UsedAt = CurrentDateUsedInTheseTests;
        await databaseContextOfThisRepositoryTest.SaveChangesAsync();
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();
        var accountReadJustBeforeTheDeactivation = await databaseContextOfThisRepositoryTest.Users.SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);

        var deactivationWasSavedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToSaveTwoFactorDeactivationOfThisAccountAsync(accountReadJustBeforeTheDeactivation, CreateSecurityEventForThisAccount(accountSavedInDatabaseByThisTest.Id, SecurityEventType.TwoFactorDeactivation));

        Assert.True(deactivationWasSavedInTheDatabase);
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();
        var accountReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.Users.AsNoTracking().SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);
        Assert.False(accountReadBackFromTheRealDatabase.IsTwoFactorEnabled);
        Assert.Null(accountReadBackFromTheRealDatabase.EncryptedTotpSecret);
        Assert.Equal(0, await CountBackupCodesOfThisAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id));
        Assert.Equal(10, await CountBackupCodesOfThisAccountAsync(databaseContextOfThisRepositoryTest, otherAccountSavedByThisTest.Id));
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id, SecurityEventType.TwoFactorDeactivation));
    }

    // Tout ou rien : un conflit ne touche ni le secret, ni les codes, ni le journal.
    [Fact]
    public async Task ConflictOnTheAccountSavesNothingOfTheDeactivation()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new TwoFactorAuthenticationRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-desactivation-conflit@exemple.test");
        await ActivateThisAccountDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id, 10);
        var accountReadJustBeforeTheConflict = await databaseContextOfThisRepositoryTest.Users.SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);
        await SimulateAnotherRequestCountingOneAttemptAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id);

        var deactivationWasSavedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToSaveTwoFactorDeactivationOfThisAccountAsync(accountReadJustBeforeTheConflict, CreateSecurityEventForThisAccount(accountSavedInDatabaseByThisTest.Id, SecurityEventType.TwoFactorDeactivation));

        Assert.False(deactivationWasSavedInTheDatabase);
        var accountReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.Users.AsNoTracking().SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);
        Assert.True(accountReadBackFromTheRealDatabase.IsTwoFactorEnabled);
        Assert.Equal(SecretOfThePhoneEncryptedInTheseTests, accountReadBackFromTheRealDatabase.EncryptedTotpSecret);
        Assert.Equal(10, await CountBackupCodesOfThisAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id));
        Assert.Equal(0, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id, SecurityEventType.TwoFactorDeactivation));
    }

    // Une désactivation qui voit un code utilisé à l'instant recommence : elle n'efface jamais une ligne qu'elle n'a pas relue.
    [Fact]
    public async Task BackupCodeUsedByAnotherRequestJustBeforeTheDeactivationSavesNothing()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-desactivation-code@exemple.test");
        await ActivateThisAccountDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id, 1);
        var accountReadJustBeforeTheDeactivation = await databaseContextOfThisRepositoryTest.Users.SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);
        var backupCodeTrackedByTheContext = await databaseContextOfThisRepositoryTest.BackupCodes.SingleAsync(backupCodeSearchedInTheDatabaseTable => backupCodeSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id);
        await databaseContextOfThisRepositoryTest.BackupCodes
            .Where(backupCodeSearchedInTheDatabaseTable => backupCodeSearchedInTheDatabaseTable.Id == backupCodeTrackedByTheContext.Id)
            .ExecuteUpdateAsync(setterOfTheColumnsOfTheCode => setterOfTheColumnsOfTheCode.SetProperty(
                backupCodeBeingUpdatedInTheDatabase => backupCodeBeingUpdatedInTheDatabase.UsedAt,
                CurrentDateUsedInTheseTests));

        var deactivationWasSavedInTheDatabase = await new TwoFactorAuthenticationRepository(databaseContextOfThisRepositoryTest).TryToSaveTwoFactorDeactivationOfThisAccountAsync(accountReadJustBeforeTheDeactivation, CreateSecurityEventForThisAccount(accountSavedInDatabaseByThisTest.Id, SecurityEventType.TwoFactorDeactivation));

        Assert.False(deactivationWasSavedInTheDatabase);
        Assert.Equal(1, await CountBackupCodesOfThisAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id));
    }

    // Le journal accepte le nouveau type d'événement : c'est le contrôle ck_evenement_type de la vraie base qui répond.
    [Fact]
    public async Task DeactivationEventIsAcceptedByTheRealJournal()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-journal-desactivation@exemple.test");

        await new SecurityEventRepository(databaseContextOfThisRepositoryTest).SaveSecurityEventInTheJournalAsync(CreateSecurityEventForThisAccount(accountSavedInDatabaseByThisTest.Id, SecurityEventType.TwoFactorDeactivation));

        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id, SecurityEventType.TwoFactorDeactivation));
    }
}
