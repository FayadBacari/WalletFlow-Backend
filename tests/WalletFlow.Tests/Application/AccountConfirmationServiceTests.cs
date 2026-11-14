using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le cas d'utilisation de la confirmation (GR Auth, SS §2), avec de faux outils.
public class AccountConfirmationServiceTests
{
    private const string RegistrationPassword = "Mot-De-Passe-Valide-1";
    private const string LinkToken = FakeSecureTokenGenerator.FixedToken;

    private readonly FakeUserAccountRepository accountRepository = new FakeUserAccountRepository();
    private readonly FakeTimeProvider clock = new FakeTimeProvider();

    // Crée le service à tester, avec ses faux outils.
    private AccountConfirmationService CreateConfirmationService()
    {
        return new AccountConfirmationService(
            accountRepository,
            new FakeDatabaseUnitOfWork(accountRepository),
            new FakePasswordHasher(),
            new FakeSecureTokenGenerator(),
            clock);
    }

    // Ajoute à la fausse base un compte non confirmé, avec son lien valable 24 heures.
    private User AddUnconfirmedAccount()
    {
        var unconfirmedAccount = new User
        {
            Email = "nouvel.utilisateur@exemple.test",
            PasswordHash = FakePasswordHasher.HashPrefix + RegistrationPassword
        };
        accountRepository.SavedUsers.Add(unconfirmedAccount);
        accountRepository.SavedEmailTokens.Add(new EmailToken
        {
            Type = EmailTokenType.SignupConfirmation,
            TokenHash = FakeSecureTokenGenerator.FixedHash,
            ExpiresAt = clock.GetUtcNow().UtcDateTime.AddHours(24),
            UserId = unconfirmedAccount.Id
        });

        return unconfirmedAccount;
    }

    // Tapé sur deux appareils différents, le même mot de passe doit confirmer le compte (SS §2).
    [Fact]
    public async Task PasswordTypedWithDecomposedAccentConfirmsTheAccount()
    {
        var service = CreateConfirmationService();
        var unconfirmedAccount = AddUnconfirmedAccount();
        unconfirmedAccount.ReplacePasswordHash(FakePasswordHasher.HashPrefix + "Mot-De-Passé-1");

        var result = await service.ConfirmAsync(
            LinkToken, "Mot-De-Passe\u0301-1");

        Assert.IsType<AccountConfirmationResult.AccountConfirmed>(result);
    }

    [Fact]
    public async Task RightLinkAndRightPasswordConfirmTheAccountAndUseTheToken()
    {
        var service = CreateConfirmationService();
        var unconfirmedAccount = AddUnconfirmedAccount();

        var result = await service.ConfirmAsync(
            LinkToken, RegistrationPassword);

        Assert.IsType<AccountConfirmationResult.AccountConfirmed>(result);
        Assert.True(unconfirmedAccount.IsEmailConfirmed);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, accountRepository.SavedEmailTokens[0].UsedAt);
    }

    // Ouvrir le lien sans connaître le mot de passe n'active rien, et le lien reste utilisable (SS §2).
    [Fact]
    public async Task WrongPasswordConfirmsNothingAndKeepsTheLinkUsable()
    {
        var service = CreateConfirmationService();
        var unconfirmedAccount = AddUnconfirmedAccount();

        var firstResult = await service.ConfirmAsync(
            LinkToken, "Autre-Mot-De-Passe-2");

        Assert.IsType<AccountConfirmationResult.WrongPassword>(firstResult);
        Assert.False(unconfirmedAccount.IsEmailConfirmed);
        Assert.Null(accountRepository.SavedEmailTokens[0].UsedAt);

        var secondResult = await service.ConfirmAsync(
            LinkToken, RegistrationPassword);

        Assert.IsType<AccountConfirmationResult.AccountConfirmed>(secondResult);
    }

    [Fact]
    public async Task UnknownLinkIsRefusedWithTheLinkMessage()
    {
        var service = CreateConfirmationService();
        var unconfirmedAccount = AddUnconfirmedAccount();

        var result = await service.ConfirmAsync(
            "jeton-inconnu", RegistrationPassword);

        Assert.IsType<AccountConfirmationResult.LinkNoLongerValid>(result);
        Assert.False(unconfirmedAccount.IsEmailConfirmed);
    }

    // Le lien vaut pendant 24 heures exactement : il marche encore une seconde avant, et plus à l'heure dite.
    [Fact]
    public async Task LinkWorksUntilOneSecondBeforeItsExpiration()
    {
        var service = CreateConfirmationService();
        AddUnconfirmedAccount();
        clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));

        var result = await service.ConfirmAsync(
            LinkToken, RegistrationPassword);

        Assert.IsType<AccountConfirmationResult.AccountConfirmed>(result);
    }

    [Fact]
    public async Task LinkIsRefusedExactlyAtItsExpiration()
    {
        var service = CreateConfirmationService();
        var unconfirmedAccount = AddUnconfirmedAccount();
        clock.Advance(TimeSpan.FromHours(24));

        var result = await service.ConfirmAsync(
            LinkToken, RegistrationPassword);

        Assert.IsType<AccountConfirmationResult.LinkNoLongerValid>(result);
        Assert.False(unconfirmedAccount.IsEmailConfirmed);
    }

    // Un lien ne sert qu'une seule fois (SS §2).
    [Fact]
    public async Task LinkAlreadyUsedIsRefusedEvenWithTheRightPassword()
    {
        var service = CreateConfirmationService();
        var unconfirmedAccount = AddUnconfirmedAccount();
        accountRepository.SavedEmailTokens[0].MarkAsUsed(clock.GetUtcNow().UtcDateTime);

        var result = await service.ConfirmAsync(
            LinkToken, RegistrationPassword);

        Assert.IsType<AccountConfirmationResult.LinkNoLongerValid>(result);
        Assert.False(unconfirmedAccount.IsEmailConfirmed);
    }

    [Fact]
    public async Task AccountAlreadyConfirmedIsRefusedWithTheLinkMessage()
    {
        var service = CreateConfirmationService();
        var confirmedAccount = AddUnconfirmedAccount();
        confirmedAccount.ConfirmEmail();

        var result = await service.ConfirmAsync(
            LinkToken, RegistrationPassword);

        Assert.IsType<AccountConfirmationResult.LinkNoLongerValid>(result);
        Assert.Null(accountRepository.SavedEmailTokens[0].UsedAt);
    }

    // Le compte a pu être effacé par la tâche quotidienne entre la lecture du jeton et celle du compte.
    [Fact]
    public async Task LinkOfAccountThatNoLongerExistsIsRefusedWithTheLinkMessage()
    {
        var service = CreateConfirmationService();
        AddUnconfirmedAccount();
        accountRepository.SavedUsers.Clear();

        var result = await service.ConfirmAsync(
            LinkToken, RegistrationPassword);

        Assert.IsType<AccountConfirmationResult.LinkNoLongerValid>(result);
    }

    // Une nouvelle inscription a pu remplacer le lien pendant la vérification du mot de passe.
    [Fact]
    public async Task LinkReplacedJustBeforeTheConfirmationIsRefusedWithTheLinkMessage()
    {
        var service = CreateConfirmationService();
        AddUnconfirmedAccount();
        accountRepository.ReplaceTokenBeforeConfirmation = true;

        var result = await service.ConfirmAsync(
            LinkToken, RegistrationPassword);

        Assert.IsType<AccountConfirmationResult.LinkNoLongerValid>(result);
    }
}
