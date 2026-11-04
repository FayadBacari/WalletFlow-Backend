using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.LocalDatabaseForTheTests;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les comptes dans la vraie base PostgreSQL locale, avec l'utilisateur de l'API.
// Chaque test travaille dans une transaction annulée à la fin : la base reste vide.
public class UserAccountRepositoryTests
{
    // Crée un nouvel utilisateur avec cette adresse, sans l'enregistrer.
    private static User CreateNewUserWithThisEmailAddress(string emailAddressOfTheNewUserToCreate)
    {
        return new User { Email = emailAddressOfTheNewUserToCreate, PasswordHash = "empreinte-de-test" };
    }

    // Crée le jeton de confirmation d'un utilisateur, sans l'enregistrer.
    private static EmailToken CreateConfirmationTokenForThisNewUser(User newUserThatReceivesTheToken)
    {
        return new EmailToken
        {
            Type = EmailTokenType.SignupConfirmation,
            TokenHash = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + new string('0', 32),
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            UserId = newUserThatReceivesTheToken.Id
        };
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
    public async Task EmailAddressIsFoundOnlyAfterTheAccountHasBeenSaved()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new UserAccountRepository(databaseContextOfThisRepositoryTest);
        var newUserToSaveInTheRealDatabase = CreateNewUserWithThisEmailAddress("test-depot-recherche@exemple.test");

        Assert.False(await repositoryBeingTestedOnTheRealDatabase.CheckThatEmailAddressIsAlreadyUsedAsync("test-depot-recherche@exemple.test"));

        await repositoryBeingTestedOnTheRealDatabase.TryToSaveNewAccountWithCategoriesAndTokenAsync(
            newUserToSaveInTheRealDatabase,
            DefaultCategories.CreateTheEightDefaultCategoriesForNewUser(newUserToSaveInTheRealDatabase.Id),
            CreateConfirmationTokenForThisNewUser(newUserToSaveInTheRealDatabase));

        Assert.True(await repositoryBeingTestedOnTheRealDatabase.CheckThatEmailAddressIsAlreadyUsedAsync("test-depot-recherche@exemple.test"));
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
}
