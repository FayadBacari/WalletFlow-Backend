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
using static WalletFlow.Tests.Infrastructure.LocalDatabaseForTheTests;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie une inscription complète avec les vrais outils : hachage, jeton et base PostgreSQL locale.
// Seul l'envoi des e-mails est remplacé. Chaque test annule sa transaction à la fin : la base reste vide.
public class AccountRegistrationRealChainTests
{
    private const string ValidPasswordChosenInTheseTests = "Mot-De-Passe-Valide-1";

    private readonly LibsodiumPasswordHasher realHasherOfThePasswordsInTheseTests = new LibsodiumPasswordHasher();
    private readonly FakeEmailSender senderThatKeepsTheEmailsInMemory = new FakeEmailSender();

    // Crée le service à tester avec les vrais outils, branchés sur cet accès à la base.
    private AccountRegistrationService CreateRegistrationServiceWithTheRealTools(WalletFlowDatabaseContext databaseContextOfThisChainTest)
    {
        return new AccountRegistrationService(
            new UserAccountRepository(databaseContextOfThisChainTest),
            realHasherOfThePasswordsInTheseTests,
            new RandomSecureTokenGenerator(),
            senderThatKeepsTheEmailsInMemory,
            new PublicWebsiteSettings("https://walletflow.test"));
    }

    // Retrouve le jeton écrit dans le lien de l'e-mail de confirmation.
    private static string ReadTokenWrittenInTheConfirmationLink(string bodyOfTheSignupConfirmationEmail)
    {
        var textWrittenAfterTheTokenMarker = bodyOfTheSignupConfirmationEmail.Split("#token=")[1];
        return textWrittenAfterTheTokenMarker.Split('\n')[0].Trim();
    }

    // Une vraie inscription enregistre un compte complet et cohérent, relu directement dans la base.
    [Theory]
    [InlineData(" Test.Chaine+Reelle_1@Mon-Exemple.test ", "test.chaine+reelle_1@mon-exemple.test")]
    [InlineData("Jean.D'Arc@Exemple.test", "jean.d'arc@exemple.test")]
    public async Task VisitorRegistrationReallySavesTheCompleteAccount(string emailAddressTypedByTheVisitorForThisTest, string emailAddressExpectedInTheDatabaseForThisTest)
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var serviceBeingTestedWithTheRealTools = CreateRegistrationServiceWithTheRealTools(databaseContextOfThisChainTest);

        var resultOfTheVisitorAccountRegistration = await serviceBeingTestedWithTheRealTools.RegisterNewAccountForVisitorAsync(
            emailAddressTypedByTheVisitorForThisTest, ValidPasswordChosenInTheseTests);

        Assert.True(resultOfTheVisitorAccountRegistration.IsAccepted);

        // On oublie les objets en mémoire : tout ce qui suit est vraiment relu dans la base.
        databaseContextOfThisChainTest.ChangeTracker.Clear();

        var userReadBackFromTheRealDatabase = await databaseContextOfThisChainTest.Users.SingleAsync(userSearchedInTheDatabaseTable =>
            userSearchedInTheDatabaseTable.Email == emailAddressExpectedInTheDatabaseForThisTest);
        Assert.False(userReadBackFromTheRealDatabase.IsEmailConfirmed);
        Assert.True(realHasherOfThePasswordsInTheseTests.CheckThatPasswordMatchesItsSecureHash(ValidPasswordChosenInTheseTests, userReadBackFromTheRealDatabase.PasswordHash));

        var categoriesReadBackFromTheRealDatabase = await databaseContextOfThisChainTest.Categories.CountAsync(categorySearchedInTheDatabaseTable =>
            categorySearchedInTheDatabaseTable.UserId == userReadBackFromTheRealDatabase.Id);
        Assert.Equal(8, categoriesReadBackFromTheRealDatabase);

        // Le jeton du lien correspond à l'empreinte enregistrée, et le jeton lui-même n'est jamais en base (SS §2).
        var tokenReadBackFromTheRealDatabase = await databaseContextOfThisChainTest.EmailTokens.SingleAsync(tokenSearchedInTheDatabaseTable =>
            tokenSearchedInTheDatabaseTable.UserId == userReadBackFromTheRealDatabase.Id);
        var emailSentToTheNewUser = Assert.Single(senderThatKeepsTheEmailsInMemory.SentEmails);
        var tokenWrittenInTheConfirmationLink = ReadTokenWrittenInTheConfirmationLink(emailSentToTheNewUser.Body);
        var expectedHashOfTheLinkToken = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tokenWrittenInTheConfirmationLink)));
        Assert.Equal(expectedHashOfTheLinkToken, tokenReadBackFromTheRealDatabase.TokenHash);
        Assert.NotEqual(tokenWrittenInTheConfirmationLink, tokenReadBackFromTheRealDatabase.TokenHash);
        Assert.Equal(EmailTokenType.SignupConfirmation, tokenReadBackFromTheRealDatabase.Type);
        Assert.InRange(tokenReadBackFromTheRealDatabase.ExpiresAt - DateTime.UtcNow, TimeSpan.FromHours(23.9), TimeSpan.FromHours(24));
    }

    // Une deuxième inscription avec la même adresse ne change rien au compte : son propriétaire est seulement prévenu.
    [Fact]
    public async Task SecondRegistrationWithTheSameAddressOnlyWarnsItsOwner()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var serviceBeingTestedWithTheRealTools = CreateRegistrationServiceWithTheRealTools(databaseContextOfThisChainTest);

        await serviceBeingTestedWithTheRealTools.RegisterNewAccountForVisitorAsync("test-chaine-doublon@exemple.test", ValidPasswordChosenInTheseTests);
        var resultOfTheSecondVisitorRegistration = await serviceBeingTestedWithTheRealTools.RegisterNewAccountForVisitorAsync("test-chaine-doublon@exemple.test", "Autre-Mot-De-Passe-2");

        Assert.True(resultOfTheSecondVisitorRegistration.IsAccepted);
        databaseContextOfThisChainTest.ChangeTracker.Clear();
        var userReadBackFromTheRealDatabase = await databaseContextOfThisChainTest.Users.SingleAsync(userSearchedInTheDatabaseTable =>
            userSearchedInTheDatabaseTable.Email == "test-chaine-doublon@exemple.test");
        Assert.True(realHasherOfThePasswordsInTheseTests.CheckThatPasswordMatchesItsSecureHash(ValidPasswordChosenInTheseTests, userReadBackFromTheRealDatabase.PasswordHash));
        Assert.Equal(2, senderThatKeepsTheEmailsInMemory.SentEmails.Count);
        Assert.DoesNotContain("confirm-email", senderThatKeepsTheEmailsInMemory.SentEmails[1].Body);
    }
}
