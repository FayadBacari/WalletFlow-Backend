using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.Infrastructure.LocalDatabaseForTheTests;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie l'inscription et la confirmation avec les vrais outils : hachage, jetons et base PostgreSQL locale.
// Seul l'envoi des e-mails est remplacé. Chaque test annule sa transaction à la fin : la base reste vide.
public class AccountRegistrationRealChainTests
{
    private const string OtherPasswordChosenInTheseTests = "Autre-Mot-De-Passe-2";

    private readonly LibsodiumPasswordHasher realHasherOfThePasswordsInTheseTests = new LibsodiumPasswordHasher();
    private readonly RandomSecureTokenGenerator realGeneratorOfTheTokensInTheseTests = new RandomSecureTokenGenerator();
    private readonly FakeEmailSender senderThatKeepsTheEmailsInMemory = new FakeEmailSender();
    private readonly FakeTimeProviderForTests clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();

    // Inscrit un visiteur comme le ferait une requête, puis renvoie le jeton du lien reçu par e-mail.
    private async Task<string> RegisterVisitorThenReadTheTokenOfTheLinkAsync(
        WalletFlowDatabaseContext databaseContextOfThisChainTest,
        string emailAddressTypedByTheVisitorForThisTest,
        string passwordChosenByTheVisitorForThisTest)
    {
        var serviceThatRegistersTheNewVisitor = new AccountRegistrationService(
            new UserAccountRepository(databaseContextOfThisChainTest),
            realHasherOfThePasswordsInTheseTests,
            realGeneratorOfTheTokensInTheseTests,
            senderThatKeepsTheEmailsInMemory,
            new PublicWebsiteSettings("https://walletflow.test"),
            new InMemoryEmailAddressRequestLimiter(clockThatOnlyMovesWhenTestsAskIt),
            clockThatOnlyMovesWhenTestsAskIt);

        var resultOfTheVisitorAccountRegistration = await serviceThatRegistersTheNewVisitor.RegisterNewAccountForVisitorAsync(
            emailAddressTypedByTheVisitorForThisTest,
            passwordChosenByTheVisitorForThisTest);

        Assert.True(resultOfTheVisitorAccountRegistration.IsAccepted);

        // Chaque requête a son propre accès à la base : on oublie les objets gardés en mémoire.
        databaseContextOfThisChainTest.ChangeTracker.Clear();

        return ReadTokenWrittenInTheConfirmationLink(senderThatKeepsTheEmailsInMemory.SentEmails.Last().Body);
    }

    // Confirme un compte comme le ferait une requête.
    private async Task<AccountConfirmationResult> ConfirmAccountWithThisLinkAndPasswordAsync(
        WalletFlowDatabaseContext databaseContextOfThisChainTest,
        string tokenReadInTheConfirmationLink,
        string passwordTypedOnTheConfirmationPage)
    {
        var serviceThatConfirmsTheVisitorAccount = new AccountConfirmationService(
            new UserAccountRepository(databaseContextOfThisChainTest),
            realHasherOfThePasswordsInTheseTests,
            realGeneratorOfTheTokensInTheseTests,
            clockThatOnlyMovesWhenTestsAskIt);

        var resultOfTheConfirmationOfTheAccount = await serviceThatConfirmsTheVisitorAccount.ConfirmAccountWithLinkTokenAndPasswordAsync(
            tokenReadInTheConfirmationLink,
            passwordTypedOnTheConfirmationPage);

        databaseContextOfThisChainTest.ChangeTracker.Clear();

        return resultOfTheConfirmationOfTheAccount;
    }

    // Relit directement dans la base le compte qui utilise cette adresse.
    private static async Task<User> ReadAccountBackFromTheDatabaseAsync(WalletFlowDatabaseContext databaseContextOfThisChainTest, string emailAddressInItsStoredForm)
    {
        return await databaseContextOfThisChainTest.Users.AsNoTracking().SingleAsync(userSearchedInTheDatabaseTable =>
            userSearchedInTheDatabaseTable.Email == emailAddressInItsStoredForm);
    }

    // Une vraie inscription enregistre un compte complet et cohérent, relu directement dans la base.
    [Theory]
    [InlineData(" Test.Chaine+Reelle_1@Mon-Exemple.test ", "test.chaine+reelle_1@mon-exemple.test")]
    [InlineData("Jean.D'Arc@Exemple.test", "jean.d'arc@exemple.test")]
    public async Task VisitorRegistrationReallySavesTheCompleteAccount(string emailAddressTypedByTheVisitorForThisTest, string emailAddressExpectedInTheDatabaseForThisTest)
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();

        var tokenWrittenInTheConfirmationLink = await RegisterVisitorThenReadTheTokenOfTheLinkAsync(
            databaseContextOfThisChainTest, emailAddressTypedByTheVisitorForThisTest, ValidPasswordChosenInTheseTests);

        var userReadBackFromTheRealDatabase = await ReadAccountBackFromTheDatabaseAsync(databaseContextOfThisChainTest, emailAddressExpectedInTheDatabaseForThisTest);
        Assert.False(userReadBackFromTheRealDatabase.IsEmailConfirmed);
        Assert.True(realHasherOfThePasswordsInTheseTests.CheckThatPasswordMatchesItsSecureHash(ValidPasswordChosenInTheseTests, userReadBackFromTheRealDatabase.PasswordHash));

        var categoriesReadBackFromTheRealDatabase = await databaseContextOfThisChainTest.Categories.CountAsync(categorySearchedInTheDatabaseTable =>
            categorySearchedInTheDatabaseTable.UserId == userReadBackFromTheRealDatabase.Id);
        Assert.Equal(8, categoriesReadBackFromTheRealDatabase);

        // Le jeton du lien correspond à l'empreinte enregistrée, et le jeton lui-même n'est jamais en base (SS §2).
        var tokenReadBackFromTheRealDatabase = await databaseContextOfThisChainTest.EmailTokens.SingleAsync(tokenSearchedInTheDatabaseTable =>
            tokenSearchedInTheDatabaseTable.UserId == userReadBackFromTheRealDatabase.Id);
        var expectedHashOfTheLinkToken = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tokenWrittenInTheConfirmationLink)));
        Assert.Equal(expectedHashOfTheLinkToken, tokenReadBackFromTheRealDatabase.TokenHash);
        Assert.NotEqual(tokenWrittenInTheConfirmationLink, tokenReadBackFromTheRealDatabase.TokenHash);
        Assert.Equal(EmailTokenType.SignupConfirmation, tokenReadBackFromTheRealDatabase.Type);
        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddHours(24), tokenReadBackFromTheRealDatabase.ExpiresAt);
    }

    // Le bon lien et le bon mot de passe activent le compte, et le lien ne sert qu'une fois (GR Auth, SS §2).
    [Fact]
    public async Task RightLinkAndPasswordReallyConfirmTheAccountOnlyOnce()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var tokenWrittenInTheConfirmationLink = await RegisterVisitorThenReadTheTokenOfTheLinkAsync(
            databaseContextOfThisChainTest, "test-chaine-confirmation@exemple.test", ValidPasswordChosenInTheseTests);

        var resultOfTheFirstAccountConfirmation = await ConfirmAccountWithThisLinkAndPasswordAsync(databaseContextOfThisChainTest, tokenWrittenInTheConfirmationLink, ValidPasswordChosenInTheseTests);
        var resultOfTheSecondAccountConfirmation = await ConfirmAccountWithThisLinkAndPasswordAsync(databaseContextOfThisChainTest, tokenWrittenInTheConfirmationLink, ValidPasswordChosenInTheseTests);

        Assert.True(resultOfTheFirstAccountConfirmation.IsConfirmed);
        Assert.Equal(AccountConfirmationService.ErrorMessageForLinkNoLongerValid, resultOfTheSecondAccountConfirmation.LinkErrorMessage);
        var userReadBackFromTheRealDatabase = await ReadAccountBackFromTheDatabaseAsync(databaseContextOfThisChainTest, "test-chaine-confirmation@exemple.test");
        Assert.True(userReadBackFromTheRealDatabase.IsEmailConfirmed);
    }

    [Fact]
    public async Task WrongPasswordLeavesTheAccountUnconfirmedAndTheLinkUsable()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var tokenWrittenInTheConfirmationLink = await RegisterVisitorThenReadTheTokenOfTheLinkAsync(
            databaseContextOfThisChainTest, "test-chaine-mauvais-mot@exemple.test", ValidPasswordChosenInTheseTests);

        var resultWithTheWrongVisitorPassword = await ConfirmAccountWithThisLinkAndPasswordAsync(databaseContextOfThisChainTest, tokenWrittenInTheConfirmationLink, OtherPasswordChosenInTheseTests);

        Assert.Equal(AccountConfirmationService.ErrorMessageForWrongPasswordOnConfirmation, resultWithTheWrongVisitorPassword.PasswordErrorMessage);
        var userReadBackFromTheRealDatabase = await ReadAccountBackFromTheDatabaseAsync(databaseContextOfThisChainTest, "test-chaine-mauvais-mot@exemple.test");
        Assert.False(userReadBackFromTheRealDatabase.IsEmailConfirmed);

        var resultWithTheRightVisitorPassword = await ConfirmAccountWithThisLinkAndPasswordAsync(databaseContextOfThisChainTest, tokenWrittenInTheConfirmationLink, ValidPasswordChosenInTheseTests);
        Assert.True(resultWithTheRightVisitorPassword.IsConfirmed);
    }

    // Une nouvelle inscription tue l'ancien lien et l'ancien mot de passe, sans créer de doublon (GR Auth).
    [Fact]
    public async Task NewRegistrationOnUnconfirmedAddressReplacesThePasswordAndTheLink()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var tokenOfTheFirstConfirmationLink = await RegisterVisitorThenReadTheTokenOfTheLinkAsync(databaseContextOfThisChainTest, "test-chaine-remplacement@exemple.test", ValidPasswordChosenInTheseTests);
        var tokenOfTheSecondConfirmationLink = await RegisterVisitorThenReadTheTokenOfTheLinkAsync(databaseContextOfThisChainTest, "test-chaine-remplacement@exemple.test", OtherPasswordChosenInTheseTests);

        var resultWithTheFirstConfirmationLink = await ConfirmAccountWithThisLinkAndPasswordAsync(databaseContextOfThisChainTest, tokenOfTheFirstConfirmationLink, OtherPasswordChosenInTheseTests);
        var resultWithTheFirstChosenPassword = await ConfirmAccountWithThisLinkAndPasswordAsync(databaseContextOfThisChainTest, tokenOfTheSecondConfirmationLink, ValidPasswordChosenInTheseTests);
        var resultWithTheSecondLinkAndPassword = await ConfirmAccountWithThisLinkAndPasswordAsync(databaseContextOfThisChainTest, tokenOfTheSecondConfirmationLink, OtherPasswordChosenInTheseTests);

        Assert.Equal(AccountConfirmationService.ErrorMessageForLinkNoLongerValid, resultWithTheFirstConfirmationLink.LinkErrorMessage);
        Assert.Equal(AccountConfirmationService.ErrorMessageForWrongPasswordOnConfirmation, resultWithTheFirstChosenPassword.PasswordErrorMessage);
        Assert.True(resultWithTheSecondLinkAndPassword.IsConfirmed);

        var userReadBackFromTheRealDatabase = await ReadAccountBackFromTheDatabaseAsync(databaseContextOfThisChainTest, "test-chaine-remplacement@exemple.test");
        var categoriesReadBackFromTheRealDatabase = await databaseContextOfThisChainTest.Categories.CountAsync(categorySearchedInTheDatabaseTable =>
            categorySearchedInTheDatabaseTable.UserId == userReadBackFromTheRealDatabase.Id);
        Assert.Equal(8, categoriesReadBackFromTheRealDatabase);
    }

    // Le scénario que ces règles ferment : un inconnu inscrit l'adresse d'une autre personne avec son mot de passe.
    [Fact]
    public async Task StrangerCannotTakeOverTheAddressOfSomeoneElse()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        const string passwordChosenByTheStrangerInThisTest = "Mot-De-Passe-De-L-Inconnu-3";
        const string emailAddressOfTheRealOwner = "test-chaine-inconnu@exemple.test";

        // L'inconnu inscrit l'adresse : le vrai propriétaire reçoit le lien, mais ne connaît pas ce mot de passe.
        var tokenSentToTheRealOwner = await RegisterVisitorThenReadTheTokenOfTheLinkAsync(databaseContextOfThisChainTest, emailAddressOfTheRealOwner, passwordChosenByTheStrangerInThisTest);
        var resultOfTheClickByTheRealOwner = await ConfirmAccountWithThisLinkAndPasswordAsync(databaseContextOfThisChainTest, tokenSentToTheRealOwner, ValidPasswordChosenInTheseTests);
        Assert.False(resultOfTheClickByTheRealOwner.IsConfirmed);

        // Le vrai propriétaire s'inscrit normalement : le mot de passe de l'inconnu disparaît.
        var tokenOfTheRegistrationByTheOwner = await RegisterVisitorThenReadTheTokenOfTheLinkAsync(databaseContextOfThisChainTest, emailAddressOfTheRealOwner, ValidPasswordChosenInTheseTests);
        var resultOfTheConfirmationByTheOwner = await ConfirmAccountWithThisLinkAndPasswordAsync(databaseContextOfThisChainTest, tokenOfTheRegistrationByTheOwner, ValidPasswordChosenInTheseTests);
        Assert.True(resultOfTheConfirmationByTheOwner.IsConfirmed);

        // L'inconnu réessaie : le compte confirmé ne change pas, et le propriétaire est seulement prévenu.
        await RegisterVisitorThenReadTheTokenOfTheLinkAndAcceptItsAbsenceAsync(databaseContextOfThisChainTest, emailAddressOfTheRealOwner, passwordChosenByTheStrangerInThisTest);
        var userReadBackFromTheRealDatabase = await ReadAccountBackFromTheDatabaseAsync(databaseContextOfThisChainTest, emailAddressOfTheRealOwner);
        Assert.True(realHasherOfThePasswordsInTheseTests.CheckThatPasswordMatchesItsSecureHash(ValidPasswordChosenInTheseTests, userReadBackFromTheRealDatabase.PasswordHash));
        Assert.False(realHasherOfThePasswordsInTheseTests.CheckThatPasswordMatchesItsSecureHash(passwordChosenByTheStrangerInThisTest, userReadBackFromTheRealDatabase.PasswordHash));
        Assert.DoesNotContain("confirm-email", senderThatKeepsTheEmailsInMemory.SentEmails.Last().Body);
    }

    // Inscrit un visiteur sur une adresse déjà confirmée : aucun lien n'est attendu dans l'e-mail.
    private async Task RegisterVisitorThenReadTheTokenOfTheLinkAndAcceptItsAbsenceAsync(
        WalletFlowDatabaseContext databaseContextOfThisChainTest,
        string emailAddressTypedByTheVisitorForThisTest,
        string passwordChosenByTheVisitorForThisTest)
    {
        var serviceThatRegistersTheNewVisitor = new AccountRegistrationService(
            new UserAccountRepository(databaseContextOfThisChainTest),
            realHasherOfThePasswordsInTheseTests,
            realGeneratorOfTheTokensInTheseTests,
            senderThatKeepsTheEmailsInMemory,
            new PublicWebsiteSettings("https://walletflow.test"),
            new InMemoryEmailAddressRequestLimiter(clockThatOnlyMovesWhenTestsAskIt),
            clockThatOnlyMovesWhenTestsAskIt);

        var resultOfTheVisitorAccountRegistration = await serviceThatRegistersTheNewVisitor.RegisterNewAccountForVisitorAsync(
            emailAddressTypedByTheVisitorForThisTest,
            passwordChosenByTheVisitorForThisTest);

        Assert.True(resultOfTheVisitorAccountRegistration.IsAccepted);
        databaseContextOfThisChainTest.ChangeTracker.Clear();
    }

    // Une fois le lien expiré, le compte ne s'active plus, la tâche l'efface, et l'adresse redevient libre (GR Auth).
    [Fact]
    public async Task ExpiredLinkConfirmsNothingAndTheCleanupFreesTheAddress()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var tokenOfTheExpiredConfirmationLink = await RegisterVisitorThenReadTheTokenOfTheLinkAsync(databaseContextOfThisChainTest, "test-chaine-expire@exemple.test", ValidPasswordChosenInTheseTests);
        var userSavedJustBeforeTheExpiration = await ReadAccountBackFromTheDatabaseAsync(databaseContextOfThisChainTest, "test-chaine-expire@exemple.test");

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromHours(24));

        var resultWithTheExpiredConfirmationLink = await ConfirmAccountWithThisLinkAndPasswordAsync(databaseContextOfThisChainTest, tokenOfTheExpiredConfirmationLink, ValidPasswordChosenInTheseTests);
        Assert.Equal(AccountConfirmationService.ErrorMessageForLinkNoLongerValid, resultWithTheExpiredConfirmationLink.LinkErrorMessage);

        var serviceThatDeletesTheUnconfirmedAccounts = new UnconfirmedAccountsCleanupService(new UserAccountRepository(databaseContextOfThisChainTest), clockThatOnlyMovesWhenTestsAskIt);
        await serviceThatDeletesTheUnconfirmedAccounts.DeleteUnconfirmedAccountsWithExpiredLinkAsync();

        Assert.False(await databaseContextOfThisChainTest.Users.AnyAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == userSavedJustBeforeTheExpiration.Id));
        Assert.False(await databaseContextOfThisChainTest.Categories.AnyAsync(categorySearchedInTheDatabaseTable => categorySearchedInTheDatabaseTable.UserId == userSavedJustBeforeTheExpiration.Id));

        var tokenOfTheNewVisitorRegistration = await RegisterVisitorThenReadTheTokenOfTheLinkAsync(databaseContextOfThisChainTest, "test-chaine-expire@exemple.test", OtherPasswordChosenInTheseTests);
        var resultOfTheNewAccountConfirmation = await ConfirmAccountWithThisLinkAndPasswordAsync(databaseContextOfThisChainTest, tokenOfTheNewVisitorRegistration, OtherPasswordChosenInTheseTests);
        Assert.True(resultOfTheNewAccountConfirmation.IsConfirmed);
    }
}
