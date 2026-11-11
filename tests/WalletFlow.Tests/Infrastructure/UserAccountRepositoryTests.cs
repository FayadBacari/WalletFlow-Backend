using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les comptes dans la vraie base PostgreSQL locale, avec l'utilisateur de l'API.
// Chaque test travaille dans une transaction annulée à la fin : la base reste vide.
public class UserAccountRepositoryTests
{
    private const string OriginalHash = "empreinte-de-test";

    private static readonly DateTime Now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    // Crée un nouvel utilisateur avec cette adresse, sans l'enregistrer.
    private static User CreateUser(string emailAddress)
    {
        return new User { Email = emailAddress, PasswordHash = OriginalHash };
    }

    // Crée le jeton de confirmation d'un utilisateur, valable encore 24 heures, sans l'enregistrer.
    private static EmailToken CreateConfirmationToken(User newUser)
    {
        return CreateEmailToken(newUser, EmailTokenType.SignupConfirmation, Now.AddHours(24));
    }

    // Crée un jeton avec une empreinte unique, sans l'enregistrer.
    private static EmailToken CreateEmailToken(User user, EmailTokenType tokenType, DateTime expiresAt)
    {
        return new EmailToken
        {
            Type = tokenType,
            TokenHash = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + new string('0', 32),
            ExpiresAt = expiresAt,
            UserId = user.Id
        };
    }

    // Enregistre un nouveau compte complet avec cette adresse, puis oublie les objets gardés en mémoire.
    private static async Task<User> SaveAccountAsync(
        WalletFlowDatabaseContext databaseContext,
        string emailAddress,
        DateTime expiresAt)
    {
        var repository = new UserAccountRepository(databaseContext);
        var newUser = CreateUser(emailAddress);

        await repository.TryAddAccountAsync(
            newUser,
            DefaultCategories.CreateForUser(newUser.Id),
            CreateEmailToken(newUser, EmailTokenType.SignupConfirmation, expiresAt));

        databaseContext.ChangeTracker.Clear();
        return newUser;
    }

    [Fact]
    public async Task NewAccountIsReallySavedWithItsCategoriesAndItsConfirmationToken()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var newUser = CreateUser("test-depot-enregistrement@exemple.test");

        var saved = await repository.TryAddAccountAsync(
            newUser,
            DefaultCategories.CreateForUser(newUser.Id),
            CreateConfirmationToken(newUser));

        Assert.True(saved);

        // On oublie les objets en mémoire : tout ce qui suit est vraiment relu dans la base.
        databaseContext.ChangeTracker.Clear();

        var reloadedUser = await databaseContext.Users.SingleAsync(row =>
            row.Id == newUser.Id);
        Assert.Equal("test-depot-enregistrement@exemple.test", reloadedUser.Email);
        Assert.Equal(UserRole.User, reloadedUser.Role);
        Assert.Equal(NotificationChannel.Email, reloadedUser.NotificationChannel);
        Assert.False(reloadedUser.IsEmailConfirmed);

        var reloadedCategories = await databaseContext.Categories
            .Where(row => row.UserId == newUser.Id)
            .ToListAsync();
        Assert.Equal(8, reloadedCategories.Count);

        var reloadedToken = await databaseContext.EmailTokens.SingleAsync(row =>
            row.UserId == newUser.Id);
        Assert.Equal(EmailTokenType.SignupConfirmation, reloadedToken.Type);
        Assert.Equal(DateTimeKind.Utc, reloadedToken.ExpiresAt.Kind);
    }

    [Fact]
    public async Task ListValuesAreStoredWithTheCodesOfTheDataDictionary()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var newUser = CreateUser("test-depot-codes@exemple.test");
        var userId = newUser.Id;

        await repository.TryAddAccountAsync(
            newUser,
            DefaultCategories.CreateForUser(userId),
            CreateConfirmationToken(newUser));

        var roleCode = await databaseContext.Database
            .SqlQuery<string>($"SELECT role_utilisateur AS \"Value\" FROM utilisateur WHERE id_utilisateur = {userId}")
            .SingleAsync();
        var channelCode = await databaseContext.Database
            .SqlQuery<string>($"SELECT canal_notification AS \"Value\" FROM utilisateur WHERE id_utilisateur = {userId}")
            .SingleAsync();
        var tokenTypeCode = await databaseContext.Database
            .SqlQuery<string>($"SELECT type_jeton AS \"Value\" FROM jeton WHERE id_utilisateur = {userId}")
            .SingleAsync();

        Assert.Equal("utilisateur", roleCode);
        Assert.Equal("email", channelCode);
        Assert.Equal("confirmation_inscription", tokenTypeCode);
    }

    [Fact]
    public async Task AccountIsFoundByItsAddressOnlyAfterItHasBeenSaved()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);

        Assert.Null(await repository.FindByEmailAsync("test-depot-recherche@exemple.test"));

        var savedAccount = await SaveAccountAsync(databaseContext, "test-depot-recherche@exemple.test", Now.AddHours(24));

        var foundByEmail = await repository.FindByEmailAsync("test-depot-recherche@exemple.test");
        var foundById = await repository.FindByIdAsync(savedAccount.Id);
        Assert.Equal(savedAccount.Id, foundByEmail!.Id);
        Assert.Equal("test-depot-recherche@exemple.test", foundById!.Email);
        Assert.Null(await repository.FindByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task SecondAccountWithTheSameAddressIsRefusedWithoutAnyError()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var firstUser = CreateUser("test-depot-doublon@exemple.test");
        var secondUser = CreateUser("test-depot-doublon@exemple.test");

        var firstSaved = await repository.TryAddAccountAsync(
            firstUser,
            DefaultCategories.CreateForUser(firstUser.Id),
            CreateConfirmationToken(firstUser));
        var secondSaved = await repository.TryAddAccountAsync(
            secondUser,
            DefaultCategories.CreateForUser(secondUser.Id),
            CreateConfirmationToken(secondUser));

        Assert.True(firstSaved);
        Assert.False(secondSaved);

        // Le refus ne laisse rien en attente dans l'accès à la base : le prochain enregistrement repart de zéro.
        Assert.Empty(databaseContext.ChangeTracker.Entries());

        // Le refus n'a rien laissé du second compte : ni utilisateur, ni catégories.
        var users = await databaseContext.Users.CountAsync(row =>
            row.Email == "test-depot-doublon@exemple.test");
        var categories = await databaseContext.Categories.CountAsync(row =>
            row.UserId == secondUser.Id);
        Assert.Equal(1, users);
        Assert.Equal(0, categories);
    }

    // Seule la contrainte d'unicité de l'adresse veut dire « déjà utilisée » : toute autre erreur de la base remonte.
    [Fact]
    public async Task OtherDatabaseErrorIsNotMistakenForAnAddressAlreadyUsed()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);

        // La base refuse une adresse en majuscules (contrainte ck_utilisateur_email).
        var user = CreateUser("Test-Depot-Majuscules@Exemple.test");

        await Assert.ThrowsAsync<DbUpdateException>(() => repository.TryAddAccountAsync(
            user,
            DefaultCategories.CreateForUser(user.Id),
            CreateConfirmationToken(user)));
    }

    // L'ancien lien disparaît et le nouveau le remplace, dans la même transaction (GR Auth, SS §2).
    [Fact]
    public async Task ReplacingPasswordOfUnconfirmedAccountAlsoReplacesItsToken()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        await SaveAccountAsync(databaseContext, "test-depot-remplacement@exemple.test", Now.AddHours(24));

        var account = await repository.FindByEmailAsync("test-depot-remplacement@exemple.test");
        var newToken = CreateConfirmationToken(account!);

        account!.ReplacePasswordHash("nouvelle-empreinte");
        await repository.PrepareConfirmationTokenReplacementAsync(newToken);
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.True(saved);
        databaseContext.ChangeTracker.Clear();

        var reloadedUser = await databaseContext.Users.SingleAsync(row =>
            row.Email == "test-depot-remplacement@exemple.test");
        Assert.Equal("nouvelle-empreinte", reloadedUser.PasswordHash);
        Assert.False(reloadedUser.IsEmailConfirmed);

        var reloadedToken = await databaseContext.EmailTokens.SingleAsync(row =>
            row.UserId == reloadedUser.Id);
        Assert.Equal(newToken.Id, reloadedToken.Id);
    }

    // Le remplacement n'efface que les liens de confirmation : les autres liens du compte restent.
    [Fact]
    public async Task ReplacingPasswordKeepsTheOtherTokensOfTheAccount()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveAccountAsync(databaseContext, "test-depot-autres-jetons@exemple.test", Now.AddHours(24));
        var otherToken = CreateEmailToken(savedAccount, EmailTokenType.PasswordRecovery, Now.AddHours(1));
        databaseContext.EmailTokens.Add(otherToken);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        var account = await repository.FindByEmailAsync("test-depot-autres-jetons@exemple.test");
        account!.ReplacePasswordHash("nouvelle-empreinte");
        await repository.PrepareConfirmationTokenReplacementAsync(CreateConfirmationToken(account));
        Assert.True(await TrySaveChangesAsync(databaseContext));
        databaseContext.ChangeTracker.Clear();

        Assert.True(await databaseContext.EmailTokens.AnyAsync(row => row.Id == otherToken.Id));
    }

    // Le risque le plus grave : un inconnu remplace le mot de passe d'un compte confirmé au même instant.
    [Fact]
    public async Task PasswordOfAccountConfirmedMeanwhileIsNeverReplaced()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveAccountAsync(databaseContext, "test-depot-course@exemple.test", Now.AddHours(24));

        var account = await repository.FindByEmailAsync("test-depot-course@exemple.test");

        // Une autre requête confirme le compte entre la lecture et l'écriture.
        await databaseContext.Database.ExecuteSqlAsync($"UPDATE utilisateur SET email_confirme = TRUE WHERE id_utilisateur = {savedAccount.Id}");

        account!.ReplacePasswordHash("empreinte-d-un-inconnu");
        await repository.PrepareConfirmationTokenReplacementAsync(CreateConfirmationToken(account));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.False(saved);
        Assert.Empty(databaseContext.ChangeTracker.Entries());

        var reloadedUser = await databaseContext.Users.SingleAsync(row =>
            row.Id == savedAccount.Id);
        Assert.Equal(OriginalHash, reloadedUser.PasswordHash);
        Assert.Equal(1, await databaseContext.EmailTokens.CountAsync(row =>
            row.UserId == savedAccount.Id));
    }

    [Fact]
    public async Task ConfirmationSavesTheAccountStateAndTheTokenUseTogether()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveAccountAsync(databaseContext, "test-depot-confirmation@exemple.test", Now.AddHours(24));

        var account = await repository.FindByIdAsync(savedAccount.Id);
        var token = await databaseContext.EmailTokens.SingleAsync(row =>
            row.UserId == savedAccount.Id);

        account!.ConfirmEmail();
        token.MarkAsUsed(Now);
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.True(saved);
        databaseContext.ChangeTracker.Clear();

        var reloadedUser = await databaseContext.Users.SingleAsync(row =>
            row.Id == savedAccount.Id);
        var reloadedToken = await databaseContext.EmailTokens.SingleAsync(row =>
            row.UserId == savedAccount.Id);
        Assert.True(reloadedUser.IsEmailConfirmed);
        Assert.Equal(Now, reloadedToken.UsedAt);
    }

    // Une nouvelle inscription a remplacé le lien pendant la vérification du mot de passe : rien n'est confirmé.
    [Fact]
    public async Task ConfirmationIsRefusedWhenTheTokenHasBeenReplacedMeanwhile()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveAccountAsync(databaseContext, "test-depot-lien-remplace@exemple.test", Now.AddHours(24));

        var account = await repository.FindByIdAsync(savedAccount.Id);
        var token = await databaseContext.EmailTokens.SingleAsync(row =>
            row.UserId == savedAccount.Id);

        await databaseContext.Database.ExecuteSqlAsync($"DELETE FROM jeton WHERE id_jeton = {token.Id}");

        account!.ConfirmEmail();
        token.MarkAsUsed(Now);
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.False(saved);
        var reloadedUser = await databaseContext.Users.SingleAsync(row =>
            row.Id == savedAccount.Id);
        Assert.False(reloadedUser.IsEmailConfirmed);
    }

    // Seuls les liens de confirmation d'inscription servent à confirmer un compte.
    [Fact]
    public async Task TokenSearchFindsOnlySignupConfirmationTokens()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveAccountAsync(databaseContext, "test-depot-type-jeton@exemple.test", Now.AddHours(24));
        var passwordRecoveryToken = CreateEmailToken(savedAccount, EmailTokenType.PasswordRecovery, Now.AddHours(1));
        databaseContext.EmailTokens.Add(passwordRecoveryToken);
        await databaseContext.SaveChangesAsync();
        var signupConfirmationToken = await databaseContext.EmailTokens.SingleAsync(row =>
            row.UserId == savedAccount.Id
            && row.Type == EmailTokenType.SignupConfirmation);

        Assert.Null(await repository.FindTokenAsync(passwordRecoveryToken.TokenHash, EmailTokenType.SignupConfirmation));
        Assert.NotNull(await repository.FindTokenAsync(signupConfirmationToken.TokenHash, EmailTokenType.SignupConfirmation));
        Assert.Null(await repository.FindTokenAsync(new string('f', 64), EmailTokenType.SignupConfirmation));
    }

    // La recherche d'un jeton doit utiliser l'index unique de sa colonne : sinon, chaque clic sur un lien parcourt toute la table.
    [Fact]
    public async Task TokenSearchUsesTheUniqueIndexOfItsColumn()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();

        // Sur une table presque vide, PostgreSQL préfère lire toute la table : ce test le lui interdit.
        await databaseContext.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off");

        var tokenHash = new string('a', 64);
        await using var command = databaseContext.EmailTokens
            .Where(row => row.TokenHash == tokenHash)
            .CreateDbCommand();
        command.CommandText = "EXPLAIN " + command.CommandText;

        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        Assert.Contains("jeton_empreinte_jeton_key", string.Join("\n", lines));
    }

    // Le compte, ses catégories et son jeton partent ensemble une fois le lien expiré (GR Auth).
    [Fact]
    public async Task CleanupDeletesUnconfirmedAccountWhoseLinkHasExpired()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var expiredAccount = await SaveAccountAsync(databaseContext, "test-depot-expire@exemple.test", Now.AddSeconds(-1));

        var deletedCount = await repository.DeleteExpiredUnconfirmedAccountsAsync(Now);

        Assert.True(deletedCount >= 1);
        Assert.False(await databaseContext.Users.AnyAsync(row => row.Id == expiredAccount.Id));
        Assert.False(await databaseContext.Categories.AnyAsync(row => row.UserId == expiredAccount.Id));
        Assert.False(await databaseContext.EmailTokens.AnyAsync(row => row.UserId == expiredAccount.Id));
    }

    // La même frontière que la confirmation : à l'heure exacte d'expiration, le lien ne vaut plus.
    [Fact]
    public async Task CleanupDeletesAccountExactlyAtTheExpirationOfItsLink()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var expiringAccount = await SaveAccountAsync(databaseContext, "test-depot-expire-maintenant@exemple.test", Now);

        await repository.DeleteExpiredUnconfirmedAccountsAsync(Now);

        Assert.False(await databaseContext.Users.AnyAsync(row => row.Id == expiringAccount.Id));
    }

    [Fact]
    public async Task CleanupKeepsUnconfirmedAccountWhoseLinkIsStillValid()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var validAccount = await SaveAccountAsync(databaseContext, "test-depot-valide@exemple.test", Now.AddSeconds(1));

        await repository.DeleteExpiredUnconfirmedAccountsAsync(Now);

        Assert.True(await databaseContext.Users.AnyAsync(row => row.Id == validAccount.Id));
    }

    // Un compte confirmé n'est jamais effacé par cette tâche, même si son ancien lien a expiré.
    [Fact]
    public async Task CleanupKeepsConfirmedAccountWhoseOldLinkHasExpired()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var confirmedAccount = await SaveAccountAsync(databaseContext, "test-depot-confirme@exemple.test", Now.AddDays(-1));
        await databaseContext.Database.ExecuteSqlAsync($"UPDATE utilisateur SET email_confirme = TRUE WHERE id_utilisateur = {confirmedAccount.Id}");

        await repository.DeleteExpiredUnconfirmedAccountsAsync(Now);

        Assert.True(await databaseContext.Users.AnyAsync(row => row.Id == confirmedAccount.Id));
    }

    // Seul le lien de confirmation compte : un autre lien encore valable ne protège pas le compte.
    [Fact]
    public async Task CleanupIgnoresTheOtherTokensOfTheAccount()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var expiredAccount = await SaveAccountAsync(databaseContext, "test-depot-autre-jeton-valide@exemple.test", Now.AddDays(-1));
        databaseContext.EmailTokens.Add(CreateEmailToken(expiredAccount, EmailTokenType.PasswordRecovery, Now.AddHours(1)));
        await databaseContext.SaveChangesAsync();

        await repository.DeleteExpiredUnconfirmedAccountsAsync(Now);

        Assert.False(await databaseContext.Users.AnyAsync(row => row.Id == expiredAccount.Id));
    }
}
