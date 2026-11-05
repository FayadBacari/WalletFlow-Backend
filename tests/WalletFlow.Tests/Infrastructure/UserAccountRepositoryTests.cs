using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.LocalDatabaseForTheTests;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les comptes dans la vraie base PostgreSQL locale, avec l'utilisateur de l'API.
// Chaque test travaille dans une transaction annulée à la fin : la base reste vide.
public class UserAccountRepositoryTests
{
    private const string OriginalSecureHashOfTheTestAccounts = "empreinte-de-test";

    private static readonly DateTime CurrentDateUsedInTheseTests = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    // Crée un nouvel utilisateur avec cette adresse, sans l'enregistrer.
    private static User CreateNewUserWithThisEmailAddress(string emailAddressOfTheNewUserToCreate)
    {
        return new User { Email = emailAddressOfTheNewUserToCreate, PasswordHash = OriginalSecureHashOfTheTestAccounts };
    }

    // Crée le jeton de confirmation d'un utilisateur, valable encore 24 heures, sans l'enregistrer.
    private static EmailToken CreateConfirmationTokenForThisNewUser(User newUserThatReceivesTheToken)
    {
        return CreateEmailTokenForThisUserAccount(newUserThatReceivesTheToken, EmailTokenType.SignupConfirmation, CurrentDateUsedInTheseTests.AddHours(24));
    }

    // Crée un jeton avec une empreinte unique, sans l'enregistrer.
    private static EmailToken CreateEmailTokenForThisUserAccount(User userThatReceivesTheTokenToCreate, EmailTokenType typeOfTheTokenToCreate, DateTime expirationDateOfTheTokenToCreate)
    {
        return new EmailToken
        {
            Type = typeOfTheTokenToCreate,
            TokenHash = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + new string('0', 32),
            ExpiresAt = expirationDateOfTheTokenToCreate,
            UserId = userThatReceivesTheTokenToCreate.Id
        };
    }

    // Enregistre un nouveau compte complet avec cette adresse, puis oublie les objets gardés en mémoire.
    private static async Task<User> SaveNewAccountWithThisEmailAddressAsync(
        WalletFlowDatabaseContext databaseContextOfThisRepositoryTest,
        string emailAddressOfTheNewUserToCreate,
        DateTime expirationDateOfTheConfirmationToken)
    {
        var repositoryUsedToSaveTheAccount = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var newUserToSaveInTheRealDatabase = CreateNewUserWithThisEmailAddress(emailAddressOfTheNewUserToCreate);

        await repositoryUsedToSaveTheAccount.TryToSaveNewAccountWithCategoriesAndTokenAsync(
            newUserToSaveInTheRealDatabase,
            DefaultCategories.CreateTheEightDefaultCategoriesForNewUser(newUserToSaveInTheRealDatabase.Id),
            CreateEmailTokenForThisUserAccount(newUserToSaveInTheRealDatabase, EmailTokenType.SignupConfirmation, expirationDateOfTheConfirmationToken));

        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();
        return newUserToSaveInTheRealDatabase;
    }

    [Fact]
    public async Task NewAccountIsReallySavedWithItsCategoriesAndItsConfirmationToken()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var newUserToSaveInTheRealDatabase = CreateNewUserWithThisEmailAddress("test-depot-enregistrement@exemple.test");

        var newAccountWasSavedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToSaveNewAccountWithCategoriesAndTokenAsync(
            newUserToSaveInTheRealDatabase,
            DefaultCategories.CreateTheEightDefaultCategoriesForNewUser(newUserToSaveInTheRealDatabase.Id),
            CreateConfirmationTokenForThisNewUser(newUserToSaveInTheRealDatabase));

        Assert.True(newAccountWasSavedInTheDatabase);

        // On oublie les objets en mémoire : tout ce qui suit est vraiment relu dans la base.
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();

        var userReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.Users.SingleAsync(userSearchedInTheDatabaseTable =>
            userSearchedInTheDatabaseTable.Id == newUserToSaveInTheRealDatabase.Id);
        Assert.Equal("test-depot-enregistrement@exemple.test", userReadBackFromTheRealDatabase.Email);
        Assert.Equal(UserRole.User, userReadBackFromTheRealDatabase.Role);
        Assert.Equal(NotificationChannel.Email, userReadBackFromTheRealDatabase.NotificationChannel);
        Assert.False(userReadBackFromTheRealDatabase.IsEmailConfirmed);

        var categoriesReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.Categories
            .Where(categorySearchedInTheDatabaseTable => categorySearchedInTheDatabaseTable.UserId == newUserToSaveInTheRealDatabase.Id)
            .ToListAsync();
        Assert.Equal(8, categoriesReadBackFromTheRealDatabase.Count);

        var tokenReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.EmailTokens.SingleAsync(tokenSearchedInTheDatabaseTable =>
            tokenSearchedInTheDatabaseTable.UserId == newUserToSaveInTheRealDatabase.Id);
        Assert.Equal(EmailTokenType.SignupConfirmation, tokenReadBackFromTheRealDatabase.Type);
        Assert.Equal(DateTimeKind.Utc, tokenReadBackFromTheRealDatabase.ExpiresAt.Kind);
    }

    [Fact]
    public async Task ListValuesAreStoredWithTheCodesOfTheDataDictionary()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var newUserToSaveInTheRealDatabase = CreateNewUserWithThisEmailAddress("test-depot-codes@exemple.test");
        var identifierOfTheUserSavedByThisTest = newUserToSaveInTheRealDatabase.Id;

        await repositoryBeingTestedOnTheRealDatabase.TryToSaveNewAccountWithCategoriesAndTokenAsync(
            newUserToSaveInTheRealDatabase,
            DefaultCategories.CreateTheEightDefaultCategoriesForNewUser(identifierOfTheUserSavedByThisTest),
            CreateConfirmationTokenForThisNewUser(newUserToSaveInTheRealDatabase));

        var roleCodeReadDirectlyFromTheTable = await databaseContextOfThisRepositoryTest.Database
            .SqlQuery<string>($"SELECT role_utilisateur AS \"Value\" FROM utilisateur WHERE id_utilisateur = {identifierOfTheUserSavedByThisTest}")
            .SingleAsync();
        var channelCodeReadDirectlyFromTheTable = await databaseContextOfThisRepositoryTest.Database
            .SqlQuery<string>($"SELECT canal_notification AS \"Value\" FROM utilisateur WHERE id_utilisateur = {identifierOfTheUserSavedByThisTest}")
            .SingleAsync();
        var tokenTypeCodeReadDirectlyFromTheTable = await databaseContextOfThisRepositoryTest.Database
            .SqlQuery<string>($"SELECT type_jeton AS \"Value\" FROM jeton WHERE id_utilisateur = {identifierOfTheUserSavedByThisTest}")
            .SingleAsync();

        Assert.Equal("utilisateur", roleCodeReadDirectlyFromTheTable);
        Assert.Equal("email", channelCodeReadDirectlyFromTheTable);
        Assert.Equal("confirmation_inscription", tokenTypeCodeReadDirectlyFromTheTable);
    }

    [Fact]
    public async Task AccountIsFoundByItsAddressOnlyAfterItHasBeenSaved()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);

        Assert.Null(await repositoryBeingTestedOnTheRealDatabase.FindUserAccountByEmailAddressAsync("test-depot-recherche@exemple.test"));

        var accountSavedInDatabaseByThisTest = await SaveNewAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-depot-recherche@exemple.test", CurrentDateUsedInTheseTests.AddHours(24));

        var accountFoundWithItsEmailAddress = await repositoryBeingTestedOnTheRealDatabase.FindUserAccountByEmailAddressAsync("test-depot-recherche@exemple.test");
        var accountFoundWithItsUniqueIdentifier = await repositoryBeingTestedOnTheRealDatabase.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        Assert.Equal(accountSavedInDatabaseByThisTest.Id, accountFoundWithItsEmailAddress!.Id);
        Assert.Equal("test-depot-recherche@exemple.test", accountFoundWithItsUniqueIdentifier!.Email);
        Assert.Null(await repositoryBeingTestedOnTheRealDatabase.FindUserAccountByIdentifierAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task SecondAccountWithTheSameAddressIsRefusedWithoutAnyError()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var firstUserSavedWithThisAddress = CreateNewUserWithThisEmailAddress("test-depot-doublon@exemple.test");
        var secondUserTryingTheSameAddress = CreateNewUserWithThisEmailAddress("test-depot-doublon@exemple.test");

        var firstAccountWasSavedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToSaveNewAccountWithCategoriesAndTokenAsync(
            firstUserSavedWithThisAddress,
            DefaultCategories.CreateTheEightDefaultCategoriesForNewUser(firstUserSavedWithThisAddress.Id),
            CreateConfirmationTokenForThisNewUser(firstUserSavedWithThisAddress));
        var secondAccountWasSavedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToSaveNewAccountWithCategoriesAndTokenAsync(
            secondUserTryingTheSameAddress,
            DefaultCategories.CreateTheEightDefaultCategoriesForNewUser(secondUserTryingTheSameAddress.Id),
            CreateConfirmationTokenForThisNewUser(secondUserTryingTheSameAddress));

        Assert.True(firstAccountWasSavedInTheDatabase);
        Assert.False(secondAccountWasSavedInTheDatabase);

        // Le refus ne laisse rien en attente dans l'accès à la base : le prochain enregistrement repart de zéro.
        Assert.Empty(databaseContextOfThisRepositoryTest.ChangeTracker.Entries());

        // Le refus n'a rien laissé du second compte : ni utilisateur, ni catégories.
        var usersFoundWithThisEmailAddress = await databaseContextOfThisRepositoryTest.Users.CountAsync(userSearchedInTheDatabaseTable =>
            userSearchedInTheDatabaseTable.Email == "test-depot-doublon@exemple.test");
        var categoriesOfTheRefusedSecondAccount = await databaseContextOfThisRepositoryTest.Categories.CountAsync(categorySearchedInTheDatabaseTable =>
            categorySearchedInTheDatabaseTable.UserId == secondUserTryingTheSameAddress.Id);
        Assert.Equal(1, usersFoundWithThisEmailAddress);
        Assert.Equal(0, categoriesOfTheRefusedSecondAccount);
    }

    // Seule la contrainte d'unicité de l'adresse veut dire « déjà utilisée » : toute autre erreur de la base remonte.
    [Fact]
    public async Task OtherDatabaseErrorIsNotMistakenForAnAddressAlreadyUsed()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);

        // La base refuse une adresse en majuscules (contrainte ck_utilisateur_email).
        var userRefusedByAnotherDatabaseConstraint = CreateNewUserWithThisEmailAddress("Test-Depot-Majuscules@Exemple.test");

        await Assert.ThrowsAsync<DbUpdateException>(() => repositoryBeingTestedOnTheRealDatabase.TryToSaveNewAccountWithCategoriesAndTokenAsync(
            userRefusedByAnotherDatabaseConstraint,
            DefaultCategories.CreateTheEightDefaultCategoriesForNewUser(userRefusedByAnotherDatabaseConstraint.Id),
            CreateConfirmationTokenForThisNewUser(userRefusedByAnotherDatabaseConstraint)));
    }

    // L'ancien lien disparaît et le nouveau le remplace, dans la même transaction (GR Auth, SS §2).
    [Fact]
    public async Task ReplacingPasswordOfUnconfirmedAccountAlsoReplacesItsToken()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        await SaveNewAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-depot-remplacement@exemple.test", CurrentDateUsedInTheseTests.AddHours(24));

        var accountFoundBeforeTheNewRegistration = await repositoryBeingTestedOnTheRealDatabase.FindUserAccountByEmailAddressAsync("test-depot-remplacement@exemple.test");
        var newTokenOfTheUnconfirmedAccount = CreateConfirmationTokenForThisNewUser(accountFoundBeforeTheNewRegistration!);

        var accountWasUpdatedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToReplacePasswordAndTokenOfUnconfirmedAccountAsync(
            accountFoundBeforeTheNewRegistration!,
            "nouvelle-empreinte",
            newTokenOfTheUnconfirmedAccount);

        Assert.True(accountWasUpdatedInTheDatabase);
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();

        var userReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.Users.SingleAsync(userSearchedInTheDatabaseTable =>
            userSearchedInTheDatabaseTable.Email == "test-depot-remplacement@exemple.test");
        Assert.Equal("nouvelle-empreinte", userReadBackFromTheRealDatabase.PasswordHash);
        Assert.False(userReadBackFromTheRealDatabase.IsEmailConfirmed);

        var tokenReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.EmailTokens.SingleAsync(tokenSearchedInTheDatabaseTable =>
            tokenSearchedInTheDatabaseTable.UserId == userReadBackFromTheRealDatabase.Id);
        Assert.Equal(newTokenOfTheUnconfirmedAccount.Id, tokenReadBackFromTheRealDatabase.Id);
    }

    // Le remplacement n'efface que les liens de confirmation : les autres liens du compte restent.
    [Fact]
    public async Task ReplacingPasswordKeepsTheOtherTokensOfTheAccount()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-depot-autres-jetons@exemple.test", CurrentDateUsedInTheseTests.AddHours(24));
        var otherTokenOfTheAccountInThisTest = CreateEmailTokenForThisUserAccount(accountSavedInDatabaseByThisTest, EmailTokenType.PasswordRecovery, CurrentDateUsedInTheseTests.AddHours(1));
        databaseContextOfThisRepositoryTest.EmailTokens.Add(otherTokenOfTheAccountInThisTest);
        await databaseContextOfThisRepositoryTest.SaveChangesAsync();
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();

        var accountFoundBeforeTheNewRegistration = await repositoryBeingTestedOnTheRealDatabase.FindUserAccountByEmailAddressAsync("test-depot-autres-jetons@exemple.test");
        await repositoryBeingTestedOnTheRealDatabase.TryToReplacePasswordAndTokenOfUnconfirmedAccountAsync(
            accountFoundBeforeTheNewRegistration!,
            "nouvelle-empreinte",
            CreateConfirmationTokenForThisNewUser(accountFoundBeforeTheNewRegistration!));
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();

        Assert.True(await databaseContextOfThisRepositoryTest.EmailTokens.AnyAsync(tokenSearchedInTheDatabaseTable => tokenSearchedInTheDatabaseTable.Id == otherTokenOfTheAccountInThisTest.Id));
    }

    // Le risque le plus grave : un inconnu remplace le mot de passe d'un compte confirmé au même instant.
    [Fact]
    public async Task PasswordOfAccountConfirmedMeanwhileIsNeverReplaced()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-depot-course@exemple.test", CurrentDateUsedInTheseTests.AddHours(24));

        var accountReadJustBeforeTheConfirmation = await repositoryBeingTestedOnTheRealDatabase.FindUserAccountByEmailAddressAsync("test-depot-course@exemple.test");

        // Une autre requête confirme le compte entre la lecture et l'écriture.
        await databaseContextOfThisRepositoryTest.Database.ExecuteSqlAsync($"UPDATE utilisateur SET email_confirme = TRUE WHERE id_utilisateur = {accountSavedInDatabaseByThisTest.Id}");

        var accountWasUpdatedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToReplacePasswordAndTokenOfUnconfirmedAccountAsync(
            accountReadJustBeforeTheConfirmation!,
            "empreinte-d-un-inconnu",
            CreateConfirmationTokenForThisNewUser(accountReadJustBeforeTheConfirmation!));

        Assert.False(accountWasUpdatedInTheDatabase);
        Assert.Empty(databaseContextOfThisRepositoryTest.ChangeTracker.Entries());

        var userReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.Users.SingleAsync(userSearchedInTheDatabaseTable =>
            userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);
        Assert.Equal(OriginalSecureHashOfTheTestAccounts, userReadBackFromTheRealDatabase.PasswordHash);
        Assert.Equal(1, await databaseContextOfThisRepositoryTest.EmailTokens.CountAsync(tokenSearchedInTheDatabaseTable =>
            tokenSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id));
    }

    [Fact]
    public async Task ConfirmationSavesTheAccountStateAndTheTokenUseTogether()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-depot-confirmation@exemple.test", CurrentDateUsedInTheseTests.AddHours(24));

        var accountToConfirmInThisTest = await repositoryBeingTestedOnTheRealDatabase.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        var tokenOfTheAccountToConfirm = await databaseContextOfThisRepositoryTest.EmailTokens.SingleAsync(tokenSearchedInTheDatabaseTable =>
            tokenSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id);

        var accountWasConfirmedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToConfirmAccountAndUseItsTokenAsync(
            accountToConfirmInThisTest!,
            tokenOfTheAccountToConfirm,
            CurrentDateUsedInTheseTests);

        Assert.True(accountWasConfirmedInTheDatabase);
        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();

        var userReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.Users.SingleAsync(userSearchedInTheDatabaseTable =>
            userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);
        var tokenReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.EmailTokens.SingleAsync(tokenSearchedInTheDatabaseTable =>
            tokenSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id);
        Assert.True(userReadBackFromTheRealDatabase.IsEmailConfirmed);
        Assert.Equal(CurrentDateUsedInTheseTests, tokenReadBackFromTheRealDatabase.UsedAt);
    }

    // Une nouvelle inscription a remplacé le lien pendant la vérification du mot de passe : rien n'est confirmé.
    [Fact]
    public async Task ConfirmationIsRefusedWhenTheTokenHasBeenReplacedMeanwhile()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-depot-lien-remplace@exemple.test", CurrentDateUsedInTheseTests.AddHours(24));

        var accountToConfirmInThisTest = await repositoryBeingTestedOnTheRealDatabase.FindUserAccountByIdentifierAsync(accountSavedInDatabaseByThisTest.Id);
        var tokenReadJustBeforeTheReplacement = await databaseContextOfThisRepositoryTest.EmailTokens.SingleAsync(tokenSearchedInTheDatabaseTable =>
            tokenSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id);

        await databaseContextOfThisRepositoryTest.Database.ExecuteSqlAsync($"DELETE FROM jeton WHERE id_jeton = {tokenReadJustBeforeTheReplacement.Id}");

        var accountWasConfirmedInTheDatabase = await repositoryBeingTestedOnTheRealDatabase.TryToConfirmAccountAndUseItsTokenAsync(
            accountToConfirmInThisTest!,
            tokenReadJustBeforeTheReplacement,
            CurrentDateUsedInTheseTests);

        Assert.False(accountWasConfirmedInTheDatabase);
        var userReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.Users.SingleAsync(userSearchedInTheDatabaseTable =>
            userSearchedInTheDatabaseTable.Id == accountSavedInDatabaseByThisTest.Id);
        Assert.False(userReadBackFromTheRealDatabase.IsEmailConfirmed);
    }

    // Seuls les liens de confirmation d'inscription servent à confirmer un compte.
    [Fact]
    public async Task TokenSearchFindsOnlySignupConfirmationTokens()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountSavedInDatabaseByThisTest = await SaveNewAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-depot-type-jeton@exemple.test", CurrentDateUsedInTheseTests.AddHours(24));
        var passwordRecoveryTokenOfTheAccount = CreateEmailTokenForThisUserAccount(accountSavedInDatabaseByThisTest, EmailTokenType.PasswordRecovery, CurrentDateUsedInTheseTests.AddHours(1));
        databaseContextOfThisRepositoryTest.EmailTokens.Add(passwordRecoveryTokenOfTheAccount);
        await databaseContextOfThisRepositoryTest.SaveChangesAsync();
        var signupConfirmationTokenOfTheAccount = await databaseContextOfThisRepositoryTest.EmailTokens.SingleAsync(tokenSearchedInTheDatabaseTable =>
            tokenSearchedInTheDatabaseTable.UserId == accountSavedInDatabaseByThisTest.Id
            && tokenSearchedInTheDatabaseTable.Type == EmailTokenType.SignupConfirmation);

        Assert.Null(await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(passwordRecoveryTokenOfTheAccount.TokenHash, EmailTokenType.SignupConfirmation));
        Assert.NotNull(await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(signupConfirmationTokenOfTheAccount.TokenHash, EmailTokenType.SignupConfirmation));
        Assert.Null(await repositoryBeingTestedOnTheRealDatabase.FindEmailTokenOfThisTypeByItsHashAsync(new string('f', 64), EmailTokenType.SignupConfirmation));
    }

    // La recherche d'un jeton doit utiliser l'index unique de sa colonne : sinon, chaque clic sur un lien parcourt toute la table.
    [Fact]
    public async Task TokenSearchUsesTheUniqueIndexOfItsColumn()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();

        // Sur une table presque vide, PostgreSQL préfère lire toute la table : ce test le lui interdit.
        await databaseContextOfThisRepositoryTest.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off");

        var tokenHashSearchedByThisTest = new string('a', 64);
        await using var commandBuiltByEntityFrameworkCore = databaseContextOfThisRepositoryTest.EmailTokens
            .Where(tokenSearchedInTheDatabaseTable => tokenSearchedInTheDatabaseTable.TokenHash == tokenHashSearchedByThisTest)
            .CreateDbCommand();
        commandBuiltByEntityFrameworkCore.CommandText = "EXPLAIN " + commandBuiltByEntityFrameworkCore.CommandText;

        var linesOfTheDatabaseQueryPlan = new List<string>();
        await using var readerOfTheDatabaseQueryPlan = await commandBuiltByEntityFrameworkCore.ExecuteReaderAsync();
        while (await readerOfTheDatabaseQueryPlan.ReadAsync())
        {
            linesOfTheDatabaseQueryPlan.Add(readerOfTheDatabaseQueryPlan.GetString(0));
        }

        Assert.Contains("jeton_empreinte_jeton_key", string.Join("\n", linesOfTheDatabaseQueryPlan));
    }

    // Le compte, ses catégories et son jeton partent ensemble une fois le lien expiré (GR Auth).
    [Fact]
    public async Task CleanupDeletesUnconfirmedAccountWhoseLinkHasExpired()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountWithConfirmationLinkAlreadyExpired = await SaveNewAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-depot-expire@exemple.test", CurrentDateUsedInTheseTests.AddSeconds(-1));

        var numberOfAccountsDeletedByTheCleanup = await repositoryBeingTestedOnTheRealDatabase.DeleteUnconfirmedAccountsWithExpiredLinkAsync(CurrentDateUsedInTheseTests);

        Assert.True(numberOfAccountsDeletedByTheCleanup >= 1);
        Assert.False(await databaseContextOfThisRepositoryTest.Users.AnyAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountWithConfirmationLinkAlreadyExpired.Id));
        Assert.False(await databaseContextOfThisRepositoryTest.Categories.AnyAsync(categorySearchedInTheDatabaseTable => categorySearchedInTheDatabaseTable.UserId == accountWithConfirmationLinkAlreadyExpired.Id));
        Assert.False(await databaseContextOfThisRepositoryTest.EmailTokens.AnyAsync(tokenSearchedInTheDatabaseTable => tokenSearchedInTheDatabaseTable.UserId == accountWithConfirmationLinkAlreadyExpired.Id));
    }

    // La même frontière que la confirmation : à l'heure exacte d'expiration, le lien ne vaut plus.
    [Fact]
    public async Task CleanupDeletesAccountExactlyAtTheExpirationOfItsLink()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountWithConfirmationLinkExpiringNow = await SaveNewAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-depot-expire-maintenant@exemple.test", CurrentDateUsedInTheseTests);

        await repositoryBeingTestedOnTheRealDatabase.DeleteUnconfirmedAccountsWithExpiredLinkAsync(CurrentDateUsedInTheseTests);

        Assert.False(await databaseContextOfThisRepositoryTest.Users.AnyAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountWithConfirmationLinkExpiringNow.Id));
    }

    [Fact]
    public async Task CleanupKeepsUnconfirmedAccountWhoseLinkIsStillValid()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountWithConfirmationLinkStillValid = await SaveNewAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-depot-valide@exemple.test", CurrentDateUsedInTheseTests.AddSeconds(1));

        await repositoryBeingTestedOnTheRealDatabase.DeleteUnconfirmedAccountsWithExpiredLinkAsync(CurrentDateUsedInTheseTests);

        Assert.True(await databaseContextOfThisRepositoryTest.Users.AnyAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountWithConfirmationLinkStillValid.Id));
    }

    // Un compte confirmé n'est jamais effacé par cette tâche, même si son ancien lien a expiré.
    [Fact]
    public async Task CleanupKeepsConfirmedAccountWhoseOldLinkHasExpired()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountAlreadyConfirmedInThisTest = await SaveNewAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-depot-confirme@exemple.test", CurrentDateUsedInTheseTests.AddDays(-1));
        await databaseContextOfThisRepositoryTest.Database.ExecuteSqlAsync($"UPDATE utilisateur SET email_confirme = TRUE WHERE id_utilisateur = {accountAlreadyConfirmedInThisTest.Id}");

        await repositoryBeingTestedOnTheRealDatabase.DeleteUnconfirmedAccountsWithExpiredLinkAsync(CurrentDateUsedInTheseTests);

        Assert.True(await databaseContextOfThisRepositoryTest.Users.AnyAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountAlreadyConfirmedInThisTest.Id));
    }

    // Seul le lien de confirmation compte : un autre lien encore valable ne protège pas le compte.
    [Fact]
    public async Task CleanupIgnoresTheOtherTokensOfTheAccount()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var accountWithConfirmationLinkAlreadyExpired = await SaveNewAccountWithThisEmailAddressAsync(databaseContextOfThisRepositoryTest, "test-depot-autre-jeton-valide@exemple.test", CurrentDateUsedInTheseTests.AddDays(-1));
        databaseContextOfThisRepositoryTest.EmailTokens.Add(CreateEmailTokenForThisUserAccount(accountWithConfirmationLinkAlreadyExpired, EmailTokenType.PasswordRecovery, CurrentDateUsedInTheseTests.AddHours(1)));
        await databaseContextOfThisRepositoryTest.SaveChangesAsync();

        await repositoryBeingTestedOnTheRealDatabase.DeleteUnconfirmedAccountsWithExpiredLinkAsync(CurrentDateUsedInTheseTests);

        Assert.False(await databaseContextOfThisRepositoryTest.Users.AnyAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == accountWithConfirmationLinkAlreadyExpired.Id));
    }
}
