using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Emails;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie l'inscription et la confirmation avec les vrais outils : hachage, jetons et base PostgreSQL locale.
// Seul l'envoi des e-mails est remplacé. Chaque test annule sa transaction à la fin : la base reste vide.
public class AccountRegistrationRealChainTests
{
    private const string OtherPassword = "Autre-Mot-De-Passe-2";

    private readonly LibsodiumPasswordHasher passwordHasher = RealArgonHashers.CreatePasswordHasher();
    private readonly RandomSecureTokenGenerator tokenGenerator = new RandomSecureTokenGenerator();
    private readonly FakeEmailSender emailSender = new FakeEmailSender();
    private readonly FakeTimeProvider clock = new FakeTimeProvider();

    // Inscrit un visiteur comme le ferait une requête, puis renvoie le jeton du lien reçu par e-mail.
    private async Task<string> RegisterThenReadTokenAsync(
        WalletFlowDatabaseContext databaseContext,
        string emailAddress,
        string password)
    {
        var registrationService = new AccountRegistrationService(
            new UserAccountRepository(databaseContext),
            new EntityFrameworkDatabaseUnitOfWork(databaseContext),
            passwordHasher,
            tokenGenerator,
            emailSender,
            new FrenchAccountRegistrationEmails(),
            new PublicWebsiteSettings("https://walletflow.test"),
            new InMemoryEmailAddressRequestLimiter(clock),
            clock);

        var result = await registrationService.RegisterAsync(
            emailAddress,
            password);

        Assert.IsType<AccountRegistrationResult.Accepted>(result);

        // Chaque requête a son propre accès à la base : on oublie les objets gardés en mémoire.
        databaseContext.ChangeTracker.Clear();

        return ReadConfirmationToken(emailSender.SentEmails.Last().Body);
    }

    // Confirme un compte comme le ferait une requête.
    private async Task<AccountConfirmationResult> ConfirmAccountAsync(
        WalletFlowDatabaseContext databaseContext,
        string linkToken,
        string password)
    {
        var confirmationService = new AccountConfirmationService(
            new UserAccountRepository(databaseContext),
            new EntityFrameworkDatabaseUnitOfWork(databaseContext),
            passwordHasher,
            tokenGenerator,
            clock);

        var result = await confirmationService.ConfirmAsync(
            linkToken,
            password);

        databaseContext.ChangeTracker.Clear();

        return result;
    }

    // Relit directement dans la base le compte qui utilise cette adresse.
    private static async Task<User> ReadAccountAsync(WalletFlowDatabaseContext databaseContext, string email)
    {
        return await databaseContext.Users.AsNoTracking().SingleAsync(row =>
            row.Email == email);
    }

    // Une vraie inscription enregistre un compte complet et cohérent, relu directement dans la base.
    [Theory]
    [InlineData(" Test.Chaine+Reelle_1@Mon-Exemple.test ", "test.chaine+reelle_1@mon-exemple.test")]
    [InlineData("Jean.D'Arc@Exemple.test", "jean.d'arc@exemple.test")]
    public async Task VisitorRegistrationReallySavesTheCompleteAccount(string emailAddress, string expectedEmail)
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();

        var token = await RegisterThenReadTokenAsync(
            databaseContext, emailAddress, ValidPassword);

        var reloadedUser = await ReadAccountAsync(databaseContext, expectedEmail);
        Assert.False(reloadedUser.IsEmailConfirmed);
        Assert.True(await passwordHasher.VerifyAsync(ValidPassword, reloadedUser.PasswordHash));

        var reloadedCategories = await databaseContext.Categories.CountAsync(row =>
            row.UserId == reloadedUser.Id);
        Assert.Equal(8, reloadedCategories);

        // Le jeton du lien correspond à l'empreinte enregistrée, et le jeton lui-même n'est jamais en base (SS §2).
        var reloadedToken = await databaseContext.EmailTokens.SingleAsync(row =>
            row.UserId == reloadedUser.Id);
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        Assert.Equal(expectedHash, reloadedToken.TokenHash);
        Assert.NotEqual(token, reloadedToken.TokenHash);
        Assert.Equal(EmailTokenType.SignupConfirmation, reloadedToken.Type);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddHours(24), reloadedToken.ExpiresAt);
    }

    // Le bon lien et le bon mot de passe activent le compte, et le lien ne sert qu'une fois (GR Auth, SS §2).
    [Fact]
    public async Task RightLinkAndPasswordReallyConfirmTheAccountOnlyOnce()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var token = await RegisterThenReadTokenAsync(
            databaseContext, "test-chaine-confirmation@exemple.test", ValidPassword);

        var firstResult = await ConfirmAccountAsync(databaseContext, token, ValidPassword);
        var secondResult = await ConfirmAccountAsync(databaseContext, token, ValidPassword);

        Assert.IsType<AccountConfirmationResult.AccountConfirmed>(firstResult);
        Assert.IsType<AccountConfirmationResult.LinkNoLongerValid>(secondResult);
        var reloadedUser = await ReadAccountAsync(databaseContext, "test-chaine-confirmation@exemple.test");
        Assert.True(reloadedUser.IsEmailConfirmed);
    }

    [Fact]
    public async Task WrongPasswordLeavesTheAccountUnconfirmedAndTheLinkUsable()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var token = await RegisterThenReadTokenAsync(
            databaseContext, "test-chaine-mauvais-mot@exemple.test", ValidPassword);

        var wrongPasswordResult = await ConfirmAccountAsync(databaseContext, token, OtherPassword);

        Assert.IsType<AccountConfirmationResult.WrongPassword>(wrongPasswordResult);
        var reloadedUser = await ReadAccountAsync(databaseContext, "test-chaine-mauvais-mot@exemple.test");
        Assert.False(reloadedUser.IsEmailConfirmed);

        var rightPasswordResult = await ConfirmAccountAsync(databaseContext, token, ValidPassword);
        Assert.IsType<AccountConfirmationResult.AccountConfirmed>(rightPasswordResult);
    }

    // Une nouvelle inscription tue l'ancien lien et l'ancien mot de passe, sans créer de doublon (GR Auth).
    [Fact]
    public async Task NewRegistrationOnUnconfirmedAddressReplacesThePasswordAndTheLink()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var firstConfirmationToken = await RegisterThenReadTokenAsync(databaseContext, "test-chaine-remplacement@exemple.test", ValidPassword);
        var secondConfirmationToken = await RegisterThenReadTokenAsync(databaseContext, "test-chaine-remplacement@exemple.test", OtherPassword);

        var firstLinkResult = await ConfirmAccountAsync(databaseContext, firstConfirmationToken, OtherPassword);
        var firstPasswordResult = await ConfirmAccountAsync(databaseContext, secondConfirmationToken, ValidPassword);
        var secondResult = await ConfirmAccountAsync(databaseContext, secondConfirmationToken, OtherPassword);

        Assert.IsType<AccountConfirmationResult.LinkNoLongerValid>(firstLinkResult);
        Assert.IsType<AccountConfirmationResult.WrongPassword>(firstPasswordResult);
        Assert.IsType<AccountConfirmationResult.AccountConfirmed>(secondResult);

        var reloadedUser = await ReadAccountAsync(databaseContext, "test-chaine-remplacement@exemple.test");
        var reloadedCategories = await databaseContext.Categories.CountAsync(row =>
            row.UserId == reloadedUser.Id);
        Assert.Equal(8, reloadedCategories);
    }

    // Le scénario que ces règles ferment : un inconnu inscrit l'adresse d'une autre personne avec son mot de passe.
    [Fact]
    public async Task StrangerCannotTakeOverTheAddressOfSomeoneElse()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        const string strangerPassword = "Mot-De-Passe-De-L-Inconnu-3";
        const string ownerEmail = "test-chaine-inconnu@exemple.test";

        // L'inconnu inscrit l'adresse : le vrai propriétaire reçoit le lien, mais ne connaît pas ce mot de passe.
        var sentToken = await RegisterThenReadTokenAsync(databaseContext, ownerEmail, strangerPassword);
        var clickResult = await ConfirmAccountAsync(databaseContext, sentToken, ValidPassword);
        Assert.IsNotType<AccountConfirmationResult.AccountConfirmed>(clickResult);

        // Le vrai propriétaire s'inscrit normalement : le mot de passe de l'inconnu disparaît.
        var ownerToken = await RegisterThenReadTokenAsync(databaseContext, ownerEmail, ValidPassword);
        var ownerResult = await ConfirmAccountAsync(databaseContext, ownerToken, ValidPassword);
        Assert.IsType<AccountConfirmationResult.AccountConfirmed>(ownerResult);

        // L'inconnu réessaie : le compte confirmé ne change pas, et le propriétaire est seulement prévenu.
        await RegisterThenReadTokenIfAnyAsync(databaseContext, ownerEmail, strangerPassword);
        var reloadedUser = await ReadAccountAsync(databaseContext, ownerEmail);
        Assert.True(await passwordHasher.VerifyAsync(ValidPassword, reloadedUser.PasswordHash));
        Assert.False(await passwordHasher.VerifyAsync(strangerPassword, reloadedUser.PasswordHash));
        Assert.DoesNotContain("confirm-email", emailSender.SentEmails.Last().Body);
    }

    // Inscrit un visiteur sur une adresse déjà confirmée : aucun lien n'est attendu dans l'e-mail.
    private async Task RegisterThenReadTokenIfAnyAsync(
        WalletFlowDatabaseContext databaseContext,
        string emailAddress,
        string password)
    {
        var registrationService = new AccountRegistrationService(
            new UserAccountRepository(databaseContext),
            new EntityFrameworkDatabaseUnitOfWork(databaseContext),
            passwordHasher,
            tokenGenerator,
            emailSender,
            new FrenchAccountRegistrationEmails(),
            new PublicWebsiteSettings("https://walletflow.test"),
            new InMemoryEmailAddressRequestLimiter(clock),
            clock);

        var result = await registrationService.RegisterAsync(
            emailAddress,
            password);

        Assert.IsType<AccountRegistrationResult.Accepted>(result);
        databaseContext.ChangeTracker.Clear();
    }

    // Une fois le lien expiré, le compte ne s'active plus, la tâche l'efface, et l'adresse redevient libre (GR Auth).
    [Fact]
    public async Task ExpiredLinkConfirmsNothingAndTheCleanupFreesTheAddress()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var expiredConfirmationToken = await RegisterThenReadTokenAsync(databaseContext, "test-chaine-expire@exemple.test", ValidPassword);
        var user = await ReadAccountAsync(databaseContext, "test-chaine-expire@exemple.test");

        clock.Advance(TimeSpan.FromHours(24));

        var expiredLinkResult = await ConfirmAccountAsync(databaseContext, expiredConfirmationToken, ValidPassword);
        Assert.IsType<AccountConfirmationResult.LinkNoLongerValid>(expiredLinkResult);

        var cleanupService = new UnconfirmedAccountsCleanupService(new UserAccountRepository(databaseContext), clock);
        await cleanupService.DeleteExpiredUnconfirmedAccountsAsync();

        Assert.False(await databaseContext.Users.AnyAsync(row => row.Id == user.Id));
        Assert.False(await databaseContext.Categories.AnyAsync(row => row.UserId == user.Id));

        var newToken = await RegisterThenReadTokenAsync(databaseContext, "test-chaine-expire@exemple.test", OtherPassword);
        var newAccountResult = await ConfirmAccountAsync(databaseContext, newToken, OtherPassword);
        Assert.IsType<AccountConfirmationResult.AccountConfirmed>(newAccountResult);
    }
}
