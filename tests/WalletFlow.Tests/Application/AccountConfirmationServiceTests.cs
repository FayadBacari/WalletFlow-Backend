using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le cas d'utilisation de la confirmation (GR Auth, SS §2), avec de faux outils.
public class AccountConfirmationServiceTests
{
    private const string PasswordChosenAtRegistrationInTheseTests = "Mot-De-Passe-Valide-1";
    private const string TokenOfTheConfirmationLinkInTheseTests = FakeSecureTokenGenerator.TokenAlwaysReturnedByThisFakeGenerator;

    private readonly FakeUserAccountRepository repositoryThatKeepsAccountsInMemory = new FakeUserAccountRepository();
    private readonly FakeTimeProviderForTests clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();

    // Crée le service à tester, avec ses faux outils.
    private AccountConfirmationService CreateConfirmationServiceWithFakeTools()
    {
        return new AccountConfirmationService(
            repositoryThatKeepsAccountsInMemory,
            new FakePasswordHasher(),
            new FakeSecureTokenGenerator(),
            clockThatOnlyMovesWhenTestsAskIt);
    }

    // Ajoute à la fausse base un compte non confirmé, avec son lien valable 24 heures.
    private User AddUnconfirmedAccountToTheFakeDatabase()
    {
        var accountNotYetConfirmedInTheseTests = new User
        {
            Email = "nouvel.utilisateur@exemple.test",
            PasswordHash = FakePasswordHasher.PrefixOfTheFakePasswordHashes + PasswordChosenAtRegistrationInTheseTests
        };
        repositoryThatKeepsAccountsInMemory.SavedUsers.Add(accountNotYetConfirmedInTheseTests);
        repositoryThatKeepsAccountsInMemory.SavedEmailTokens.Add(new EmailToken
        {
            Type = EmailTokenType.SignupConfirmation,
            TokenHash = FakeSecureTokenGenerator.HashAlwaysReturnedByThisFakeGenerator,
            ExpiresAt = clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddHours(24),
            UserId = accountNotYetConfirmedInTheseTests.Id
        });

        return accountNotYetConfirmedInTheseTests;
    }

    [Fact]
    public async Task RightLinkAndRightPasswordConfirmTheAccountAndUseTheToken()
    {
        var serviceBeingTestedWithFakeTools = CreateConfirmationServiceWithFakeTools();
        var accountNotYetConfirmedInThisTest = AddUnconfirmedAccountToTheFakeDatabase();

        var resultOfTheConfirmationOfTheAccount = await serviceBeingTestedWithFakeTools.ConfirmAccountWithLinkTokenAndPasswordAsync(
            TokenOfTheConfirmationLinkInTheseTests, PasswordChosenAtRegistrationInTheseTests);

        Assert.True(resultOfTheConfirmationOfTheAccount.IsConfirmed);
        Assert.True(accountNotYetConfirmedInThisTest.IsEmailConfirmed);
        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime, repositoryThatKeepsAccountsInMemory.SavedEmailTokens[0].UsedAt);
    }

    // Ouvrir le lien sans connaître le mot de passe n'active rien, et le lien reste utilisable (SS §2).
    [Fact]
    public async Task WrongPasswordConfirmsNothingAndKeepsTheLinkUsable()
    {
        var serviceBeingTestedWithFakeTools = CreateConfirmationServiceWithFakeTools();
        var accountNotYetConfirmedInThisTest = AddUnconfirmedAccountToTheFakeDatabase();

        var resultOfTheFirstConfirmationAttempt = await serviceBeingTestedWithFakeTools.ConfirmAccountWithLinkTokenAndPasswordAsync(
            TokenOfTheConfirmationLinkInTheseTests, "Autre-Mot-De-Passe-2");

        Assert.False(resultOfTheFirstConfirmationAttempt.IsConfirmed);
        Assert.Equal(AccountConfirmationService.ErrorMessageForWrongPasswordOnConfirmation, resultOfTheFirstConfirmationAttempt.PasswordErrorMessage);
        Assert.Null(resultOfTheFirstConfirmationAttempt.LinkErrorMessage);
        Assert.False(accountNotYetConfirmedInThisTest.IsEmailConfirmed);
        Assert.Null(repositoryThatKeepsAccountsInMemory.SavedEmailTokens[0].UsedAt);

        var resultOfTheSecondConfirmationAttempt = await serviceBeingTestedWithFakeTools.ConfirmAccountWithLinkTokenAndPasswordAsync(
            TokenOfTheConfirmationLinkInTheseTests, PasswordChosenAtRegistrationInTheseTests);

        Assert.True(resultOfTheSecondConfirmationAttempt.IsConfirmed);
    }

    [Fact]
    public async Task UnknownLinkIsRefusedWithTheLinkMessage()
    {
        var serviceBeingTestedWithFakeTools = CreateConfirmationServiceWithFakeTools();
        var accountNotYetConfirmedInThisTest = AddUnconfirmedAccountToTheFakeDatabase();

        var resultOfTheConfirmationOfTheAccount = await serviceBeingTestedWithFakeTools.ConfirmAccountWithLinkTokenAndPasswordAsync(
            "jeton-inconnu", PasswordChosenAtRegistrationInTheseTests);

        Assert.Equal(AccountConfirmationService.ErrorMessageForLinkNoLongerValid, resultOfTheConfirmationOfTheAccount.LinkErrorMessage);
        Assert.Null(resultOfTheConfirmationOfTheAccount.PasswordErrorMessage);
        Assert.False(accountNotYetConfirmedInThisTest.IsEmailConfirmed);
    }

    // Le lien vaut pendant 24 heures exactement : il marche encore une seconde avant, et plus à l'heure dite.
    [Fact]
    public async Task LinkWorksUntilOneSecondBeforeItsExpiration()
    {
        var serviceBeingTestedWithFakeTools = CreateConfirmationServiceWithFakeTools();
        AddUnconfirmedAccountToTheFakeDatabase();
        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));

        var resultOfTheConfirmationOfTheAccount = await serviceBeingTestedWithFakeTools.ConfirmAccountWithLinkTokenAndPasswordAsync(
            TokenOfTheConfirmationLinkInTheseTests, PasswordChosenAtRegistrationInTheseTests);

        Assert.True(resultOfTheConfirmationOfTheAccount.IsConfirmed);
    }

    [Fact]
    public async Task LinkIsRefusedExactlyAtItsExpiration()
    {
        var serviceBeingTestedWithFakeTools = CreateConfirmationServiceWithFakeTools();
        var accountNotYetConfirmedInThisTest = AddUnconfirmedAccountToTheFakeDatabase();
        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromHours(24));

        var resultOfTheConfirmationOfTheAccount = await serviceBeingTestedWithFakeTools.ConfirmAccountWithLinkTokenAndPasswordAsync(
            TokenOfTheConfirmationLinkInTheseTests, PasswordChosenAtRegistrationInTheseTests);

        Assert.Equal(AccountConfirmationService.ErrorMessageForLinkNoLongerValid, resultOfTheConfirmationOfTheAccount.LinkErrorMessage);
        Assert.False(accountNotYetConfirmedInThisTest.IsEmailConfirmed);
    }

    // Un lien ne sert qu'une seule fois (SS §2).
    [Fact]
    public async Task LinkAlreadyUsedIsRefusedEvenWithTheRightPassword()
    {
        var serviceBeingTestedWithFakeTools = CreateConfirmationServiceWithFakeTools();
        var accountNotYetConfirmedInThisTest = AddUnconfirmedAccountToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.SavedEmailTokens[0].UsedAt = clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime;

        var resultOfTheConfirmationOfTheAccount = await serviceBeingTestedWithFakeTools.ConfirmAccountWithLinkTokenAndPasswordAsync(
            TokenOfTheConfirmationLinkInTheseTests, PasswordChosenAtRegistrationInTheseTests);

        Assert.Equal(AccountConfirmationService.ErrorMessageForLinkNoLongerValid, resultOfTheConfirmationOfTheAccount.LinkErrorMessage);
        Assert.False(accountNotYetConfirmedInThisTest.IsEmailConfirmed);
    }

    [Fact]
    public async Task AccountAlreadyConfirmedIsRefusedWithTheLinkMessage()
    {
        var serviceBeingTestedWithFakeTools = CreateConfirmationServiceWithFakeTools();
        var accountAlreadyConfirmedInThisTest = AddUnconfirmedAccountToTheFakeDatabase();
        accountAlreadyConfirmedInThisTest.IsEmailConfirmed = true;

        var resultOfTheConfirmationOfTheAccount = await serviceBeingTestedWithFakeTools.ConfirmAccountWithLinkTokenAndPasswordAsync(
            TokenOfTheConfirmationLinkInTheseTests, PasswordChosenAtRegistrationInTheseTests);

        Assert.Equal(AccountConfirmationService.ErrorMessageForLinkNoLongerValid, resultOfTheConfirmationOfTheAccount.LinkErrorMessage);
        Assert.Null(repositoryThatKeepsAccountsInMemory.SavedEmailTokens[0].UsedAt);
    }

    // Le compte a pu être effacé par la tâche quotidienne entre la lecture du jeton et celle du compte.
    [Fact]
    public async Task LinkOfAccountThatNoLongerExistsIsRefusedWithTheLinkMessage()
    {
        var serviceBeingTestedWithFakeTools = CreateConfirmationServiceWithFakeTools();
        AddUnconfirmedAccountToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.SavedUsers.Clear();

        var resultOfTheConfirmationOfTheAccount = await serviceBeingTestedWithFakeTools.ConfirmAccountWithLinkTokenAndPasswordAsync(
            TokenOfTheConfirmationLinkInTheseTests, PasswordChosenAtRegistrationInTheseTests);

        Assert.Equal(AccountConfirmationService.ErrorMessageForLinkNoLongerValid, resultOfTheConfirmationOfTheAccount.LinkErrorMessage);
    }

    // Une nouvelle inscription a pu remplacer le lien pendant la vérification du mot de passe.
    [Fact]
    public async Task LinkReplacedJustBeforeTheConfirmationIsRefusedWithTheLinkMessage()
    {
        var serviceBeingTestedWithFakeTools = CreateConfirmationServiceWithFakeTools();
        AddUnconfirmedAccountToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.TokenIsReplacedJustBeforeTheConfirmation = true;

        var resultOfTheConfirmationOfTheAccount = await serviceBeingTestedWithFakeTools.ConfirmAccountWithLinkTokenAndPasswordAsync(
            TokenOfTheConfirmationLinkInTheseTests, PasswordChosenAtRegistrationInTheseTests);

        Assert.False(resultOfTheConfirmationOfTheAccount.IsConfirmed);
        Assert.Equal(AccountConfirmationService.ErrorMessageForLinkNoLongerValid, resultOfTheConfirmationOfTheAccount.LinkErrorMessage);
    }
}
