using System.Net;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.LocalDatabaseForTheTests;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les liens de récupération et de déblocage dans la vraie base PostgreSQL locale, avec l'utilisateur de l'API.
// Chaque test travaille dans une transaction annulée à la fin : la base reste vide.
public class AccountRecoveryRepositoryTests
{
    private const string OldHashOfTheAccountInTheseTests = "ancienne-empreinte-du-compte";

    private static readonly DateTime CurrentDateUsedInTheseTests = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    // Enregistre un nouveau compte confirmé avec cette adresse, puis oublie les objets gardés en mémoire.
    private static async Task<User> SaveNewConfirmedAccountWithThisEmailAddressAsync(WalletFlowDatabaseContext databaseContextOfThisRepositoryTest, string emailAddressOfTheNewAccountToSave)
    {
        var newAccountToSaveInTheRealDatabase = new User { Email = emailAddressOfTheNewAccountToSave, PasswordHash = OldHashOfTheAccountInTheseTests, IsEmailConfirmed = true };
        databaseContextOfThisRepositoryTest.Users.Add(newAccountToSaveInTheRealDatabase);
        await databaseContextOfThisRepositoryTest.SaveChangesAsync();
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();

        return newAccountToSaveInTheRealDatabase;
    }

    // Crée un jeton avec une empreinte unique, sans l'enregistrer.
    private static EmailToken CreateEmailTokenForThisAccount(User accountThatReceivesTheNewToken, EmailTokenType typeOfTheNewTokenToCreate)
    {
        return new EmailToken
        {
            Type = typeOfTheNewTokenToCreate,
            TokenHash = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + new string('0', 32),
            ExpiresAt = CurrentDateUsedInTheseTests.AddMinutes(30),
            UserId = accountThatReceivesTheNewToken.Id
        };
    }

    // Enregistre directement un jeton déjà créé, puis oublie les objets gardés en mémoire.
    private static async Task SaveThisTokenDirectlyInTheDatabaseAsync(WalletFlowDatabaseContext databaseContextOfThisRepositoryTest, EmailToken tokenToSaveInTheRealDatabase)
    {
        databaseContextOfThisRepositoryTest.EmailTokens.Add(tokenToSaveInTheRealDatabase);
        await databaseContextOfThisRepositoryTest.SaveChangesAsync();
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();
    }

    // Crée un événement du journal, sans l'enregistrer.
    private static SecurityEvent CreateSecurityEventForThisAccount(User accountConcernedByTheSecurityEvent, SecurityEventType typeOfTheSecurityEventToCreate)
    {
        return new SecurityEvent
        {
            Type = typeOfTheSecurityEventToCreate,
            Result = SecurityEventResult.Success,
            OccurredAt = CurrentDateUsedInTheseTests,
            IpAddress = IPAddress.Parse("203.0.113.7"),
            UserId = accountConcernedByTheSecurityEvent.Id
        };
    }

    // Compte les événements du journal pour ce compte.
    private static async Task<int> CountSecurityEventsOfThisAccountAsync(WalletFlowDatabaseContext databaseContextOfThisRepositoryTest, Guid identifierOfTheAccountInTheJournal)
    {
        return await databaseContextOfThisRepositoryTest.SecurityEvents.CountAsync(eventSearchedInTheDatabaseTable => eventSearchedInTheDatabaseTable.UserId == identifierOfTheAccountInTheJournal);
    }

    // Un lien ne sert jamais à un autre usage que le sien.
    [Fact]
    public async Task TokenIsFoundOnlyWithItsOwnType()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-lien-par-type@exemple.test");
        var tokenSavedInDatabaseByThisTest = CreateEmailTokenForThisAccount(accountSavedInDatabaseByThisTest, EmailTokenType.AccountUnlock);
        await SaveThisTokenDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, tokenSavedInDatabaseByThisTest);

        Assert.NotNull(await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(tokenSavedInDatabaseByThisTest.TokenHash, EmailTokenType.AccountUnlock));
        Assert.Null(await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(tokenSavedInDatabaseByThisTest.TokenHash, EmailTokenType.PasswordRecovery));
    }

    // SS §2 : le jeton est invalidé dès qu'un nouveau jeton du même type est demandé.
    [Fact]
    public async Task NewTokenReplacesOnlyTheTokenOfTheSameTypeAndTheSameAccount()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-lien-remplace@exemple.test");
        var otherAccountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-lien-autre-compte@exemple.test");
        var oldTokenOfTheSameTypeInThisTest = CreateEmailTokenForThisAccount(accountSavedInDatabaseByThisTest, EmailTokenType.PasswordRecovery);
        var tokenOfAnotherTypeInThisTest = CreateEmailTokenForThisAccount(accountSavedInDatabaseByThisTest, EmailTokenType.AccountUnlock);
        var tokenOfAnotherAccountInThisTest = CreateEmailTokenForThisAccount(otherAccountSavedInDatabaseByThisTest, EmailTokenType.PasswordRecovery);
        await SaveThisTokenDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, oldTokenOfTheSameTypeInThisTest);
        await SaveThisTokenDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, tokenOfAnotherTypeInThisTest);
        await SaveThisTokenDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, tokenOfAnotherAccountInThisTest);
        var newTokenSavedByTheRepository = CreateEmailTokenForThisAccount(accountSavedInDatabaseByThisTest, EmailTokenType.PasswordRecovery);

        await repositoryBeingTestedOnTheRealDatabase.ReplaceEmailTokenOfThisTypeForThisAccountAsync(newTokenSavedByTheRepository);

        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();
        Assert.Null(await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(oldTokenOfTheSameTypeInThisTest.TokenHash, EmailTokenType.PasswordRecovery));
        Assert.NotNull(await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(newTokenSavedByTheRepository.TokenHash, EmailTokenType.PasswordRecovery));
        Assert.NotNull(await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(tokenOfAnotherTypeInThisTest.TokenHash, EmailTokenType.AccountUnlock));
        Assert.NotNull(await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(tokenOfAnotherAccountInThisTest.TokenHash, EmailTokenType.PasswordRecovery));
    }

    // L'ancien lien vient de servir au même instant : le remplacement doit réussir quand même, sinon le nouvel e-mail ne partirait pas.
    [Fact]
    public async Task NewTokenReplacesAnOldTokenThatWasJustUsedByAnotherRequest()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-lien-utilise-et-remplace@exemple.test");
        var oldTokenJustUsedByAnotherRequest = CreateEmailTokenForThisAccount(accountSavedInDatabaseByThisTest, EmailTokenType.PasswordRecovery);
        oldTokenJustUsedByAnotherRequest.UsedAt = CurrentDateUsedInTheseTests;
        await SaveThisTokenDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, oldTokenJustUsedByAnotherRequest);
        var newTokenSavedByTheRepository = CreateEmailTokenForThisAccount(accountSavedInDatabaseByThisTest, EmailTokenType.PasswordRecovery);

        await repositoryBeingTestedOnTheRealDatabase.ReplaceEmailTokenOfThisTypeForThisAccountAsync(newTokenSavedByTheRepository);

        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();
        Assert.Null(await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(oldTokenJustUsedByAnotherRequest.TokenHash, EmailTokenType.PasswordRecovery));
        Assert.NotNull(await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(newTokenSavedByTheRepository.TokenHash, EmailTokenType.PasswordRecovery));
    }

    [Fact]
    public async Task NewPasswordIsReallySavedWithTheUsedTokenAndTheSecurityEvent()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-mot-de-passe-enregistre@exemple.test");
        var tokenSavedInDatabaseByThisTest = CreateEmailTokenForThisAccount(accountSavedInDatabaseByThisTest, EmailTokenType.PasswordRecovery);
        await SaveThisTokenDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, tokenSavedInDatabaseByThisTest);
        var accountReadBeforeThePasswordChange = await repositoryBeingTestedOnTheRealDatabase.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        var tokenReadBeforeThePasswordChange = await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(tokenSavedInDatabaseByThisTest.TokenHash, EmailTokenType.PasswordRecovery);
        accountReadBeforeThePasswordChange!.FailedLoginAttemptCount = 0;

        var newPasswordWasSavedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToSaveNewPasswordAndUseItsTokenAsync(
            accountReadBeforeThePasswordChange,
            tokenReadBeforeThePasswordChange!,
            "empreinte-du-nouveau-mot-de-passe",
            CurrentDateUsedInTheseTests,
            CreateSecurityEventForThisAccount(accountSavedInDatabaseByThisTest, SecurityEventType.PasswordChange));

        Assert.True(newPasswordWasSavedInTheDatabase);
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();
        var accountReadBackFromTheRealDatabase = await repositoryBeingTestedOnTheRealDatabase.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        var tokenReadBackFromTheRealDatabase = await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(tokenSavedInDatabaseByThisTest.TokenHash, EmailTokenType.PasswordRecovery);
        Assert.Equal("empreinte-du-nouveau-mot-de-passe", accountReadBackFromTheRealDatabase!.PasswordHash);
        Assert.Equal(CurrentDateUsedInTheseTests, tokenReadBackFromTheRealDatabase!.UsedAt);
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id));
    }

    // Tout ou rien : si le mot de passe vient de changer, ni le jeton ni le journal ne sont modifiés.
    [Fact]
    public async Task NothingIsSavedWhenAnotherRequestJustChangedThePassword()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-mot-de-passe-conflit@exemple.test");
        var tokenSavedInDatabaseByThisTest = CreateEmailTokenForThisAccount(accountSavedInDatabaseByThisTest, EmailTokenType.PasswordRecovery);
        await SaveThisTokenDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, tokenSavedInDatabaseByThisTest);
        var accountReadBeforeThePasswordChange = await repositoryBeingTestedOnTheRealDatabase.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        var tokenReadBeforeThePasswordChange = await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(tokenSavedInDatabaseByThisTest.TokenHash, EmailTokenType.PasswordRecovery);

        await databaseContextOfThisRepositoryTest.Users
            .Where(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id)
            .ExecuteUpdateAsync(setterOfTheColumnsOfTheUser => setterOfTheColumnsOfTheUser.SetProperty(
                userBeingUpdatedInTheDatabase => userBeingUpdatedInTheDatabase.PasswordHash,
                "empreinte-d-une-autre-requete"));

        var newPasswordWasSavedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToSaveNewPasswordAndUseItsTokenAsync(
            accountReadBeforeThePasswordChange!,
            tokenReadBeforeThePasswordChange!,
            "empreinte-du-nouveau-mot-de-passe",
            CurrentDateUsedInTheseTests,
            CreateSecurityEventForThisAccount(accountSavedInDatabaseByThisTest, SecurityEventType.PasswordChange));

        Assert.False(newPasswordWasSavedInTheDatabase);
        var accountReadAgainAfterTheConflict = await repositoryBeingTestedOnTheRealDatabase.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        var tokenReadAgainAfterTheConflict = await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(tokenSavedInDatabaseByThisTest.TokenHash, EmailTokenType.PasswordRecovery);
        Assert.Equal("empreinte-d-une-autre-requete", accountReadAgainAfterTheConflict!.PasswordHash);
        Assert.Null(tokenReadAgainAfterTheConflict!.UsedAt);
        Assert.Equal(0, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id));
    }

    // Usage unique contrôlé en base : deux clics simultanés sur le même lien ne peuvent pas réussir tous les deux.
    [Fact]
    public async Task UnlockIsNotSavedWhenAnotherRequestJustUsedTheSameLink()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-deblocage-conflit@exemple.test");
        var tokenSavedInDatabaseByThisTest = CreateEmailTokenForThisAccount(accountSavedInDatabaseByThisTest, EmailTokenType.AccountUnlock);
        await SaveThisTokenDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, tokenSavedInDatabaseByThisTest);
        var accountReadBeforeTheAccountUnlock = await repositoryBeingTestedOnTheRealDatabase.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        var tokenReadBeforeTheAccountUnlock = await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(tokenSavedInDatabaseByThisTest.TokenHash, EmailTokenType.AccountUnlock);

        await databaseContextOfThisRepositoryTest.EmailTokens
            .Where(tokenSearchedInTheDatabaseTable => tokenSearchedInTheDatabaseTable.Id == tokenSavedInDatabaseByThisTest.Id)
            .ExecuteUpdateAsync(setterOfTheColumnsOfTheToken => setterOfTheColumnsOfTheToken.SetProperty(
                tokenBeingUpdatedInTheDatabase => tokenBeingUpdatedInTheDatabase.UsedAt,
                CurrentDateUsedInTheseTests));

        var accountWasUnlockedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToUnlockAccountAndUseItsTokenAsync(
            accountReadBeforeTheAccountUnlock!,
            tokenReadBeforeTheAccountUnlock!,
            CurrentDateUsedInTheseTests.AddMinutes(1),
            CreateSecurityEventForThisAccount(accountSavedInDatabaseByThisTest, SecurityEventType.Unlock));

        Assert.False(accountWasUnlockedInTheDatabase);
        Assert.Equal(0, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id));
    }

    // Un nouveau blocage a remplacé le lien : l'ancien lien ne doit plus rien débloquer.
    [Fact]
    public async Task UnlockIsNotSavedWhenTheLinkWasJustReplaced()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-deblocage-remplace@exemple.test");
        var tokenSavedInDatabaseByThisTest = CreateEmailTokenForThisAccount(accountSavedInDatabaseByThisTest, EmailTokenType.AccountUnlock);
        await SaveThisTokenDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, tokenSavedInDatabaseByThisTest);
        var accountReadBeforeTheAccountUnlock = await repositoryBeingTestedOnTheRealDatabase.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        var tokenReadBeforeTheAccountUnlock = await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(tokenSavedInDatabaseByThisTest.TokenHash, EmailTokenType.AccountUnlock);

        await databaseContextOfThisRepositoryTest.EmailTokens
            .Where(tokenSearchedInTheDatabaseTable => tokenSearchedInTheDatabaseTable.Id == tokenSavedInDatabaseByThisTest.Id)
            .ExecuteDeleteAsync();

        var accountWasUnlockedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToUnlockAccountAndUseItsTokenAsync(
            accountReadBeforeTheAccountUnlock!,
            tokenReadBeforeTheAccountUnlock!,
            CurrentDateUsedInTheseTests,
            CreateSecurityEventForThisAccount(accountSavedInDatabaseByThisTest, SecurityEventType.Unlock));

        Assert.False(accountWasUnlockedInTheDatabase);
    }

    [Fact]
    public async Task UnlockIsReallySavedWithTheCountersTheUsedTokenAndTheSecurityEvent()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewConfirmedAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-deblocage-enregistre@exemple.test");
        var tokenSavedInDatabaseByThisTest = CreateEmailTokenForThisAccount(accountSavedInDatabaseByThisTest, EmailTokenType.AccountUnlock);
        await SaveThisTokenDirectlyInTheDatabaseAsync(databaseContextOfThisRepositoryTest, tokenSavedInDatabaseByThisTest);
        await databaseContextOfThisRepositoryTest.Users
            .Where(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id)
            .ExecuteUpdateAsync(setterOfTheColumnsOfTheUser => setterOfTheColumnsOfTheUser
                .SetProperty(userBeingUpdatedInTheDatabase => userBeingUpdatedInTheDatabase.FailedLoginAttemptCount, (short)5)
                .SetProperty(userBeingUpdatedInTheDatabase => userBeingUpdatedInTheDatabase.LockoutCount, (short)2)
                .SetProperty(userBeingUpdatedInTheDatabase => userBeingUpdatedInTheDatabase.LockoutEndsAt, CurrentDateUsedInTheseTests.AddMinutes(30)));
        var accountReadBeforeTheAccountUnlock = await repositoryBeingTestedOnTheRealDatabase.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        var tokenReadBeforeTheAccountUnlock = await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(tokenSavedInDatabaseByThisTest.TokenHash, EmailTokenType.AccountUnlock);
        AccountLockoutRules.RemoveCurrentLockoutOfThisAccount(accountReadBeforeTheAccountUnlock!);

        var accountWasUnlockedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToUnlockAccountAndUseItsTokenAsync(
            accountReadBeforeTheAccountUnlock!,
            tokenReadBeforeTheAccountUnlock!,
            CurrentDateUsedInTheseTests,
            CreateSecurityEventForThisAccount(accountSavedInDatabaseByThisTest, SecurityEventType.Unlock));

        Assert.True(accountWasUnlockedInTheDatabase);
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();
        var accountReadBackFromTheRealDatabase = await repositoryBeingTestedOnTheRealDatabase.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        var tokenReadBackFromTheRealDatabase = await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(tokenSavedInDatabaseByThisTest.TokenHash, EmailTokenType.AccountUnlock);
        Assert.Equal(0, accountReadBackFromTheRealDatabase!.FailedLoginAttemptCount);
        Assert.Null(accountReadBackFromTheRealDatabase.LockoutEndsAt);
        Assert.Equal(2, accountReadBackFromTheRealDatabase.LockoutCount);
        Assert.Equal(CurrentDateUsedInTheseTests, tokenReadBackFromTheRealDatabase!.UsedAt);
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisRepositoryTest, accountSavedInDatabaseByThisTest.Id));
    }
}
