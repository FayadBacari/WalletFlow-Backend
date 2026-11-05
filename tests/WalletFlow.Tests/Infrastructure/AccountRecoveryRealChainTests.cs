using System.Net;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.Infrastructure.LocalDatabaseForTheTests;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la récupération et le déblocage avec les vrais outils : Argon2, jetons et base PostgreSQL locale.
// Seuls l'envoi des e-mails, la file et l'horloge sont remplacés. Chaque test annule sa transaction à la fin : la base reste vide.
public class AccountRecoveryRealChainTests
{
    private const string NewPasswordChosenInTheseTests = "Nouveau-Mot-De-Passe-2";

    private static readonly IPAddress IpAddressOfTheVisitorInTheseTests = IPAddress.Parse("203.0.113.7");

    private readonly LibsodiumPasswordHasher realHasherOfThePasswordsInTheseTests = new LibsodiumPasswordHasher();
    private readonly RandomSecureTokenGenerator realGeneratorOfTheTokensInTheseTests = new RandomSecureTokenGenerator();
    private readonly FakeEmailSender senderThatKeepsTheEmailsInMemory = new FakeEmailSender();
    private readonly FakeBackgroundEmailRequestQueue queueThatKeepsTheEmailRequestsInMemory = new FakeBackgroundEmailRequestQueue();
    private readonly FakeTimeProviderForTests clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();

    // Enregistre un compte avec ce mot de passe, haché avec le vrai Argon2.
    private async Task<Guid> SaveAccountWithThisPasswordAsync(WalletFlowDatabaseContext databaseContextOfThisChainTest, string emailAddressOfTheNewAccount, string passwordChosenForTheNewAccount, bool emailAddressOfTheAccountIsConfirmed = true)
    {
        var newAccountToSaveInTheRealDatabase = new User
        {
            Email = emailAddressOfTheNewAccount,
            PasswordHash = realHasherOfThePasswordsInTheseTests.ConvertPasswordIntoItsSecureHash(passwordChosenForTheNewAccount),
            IsEmailConfirmed = emailAddressOfTheAccountIsConfirmed
        };
        databaseContextOfThisChainTest.Users.Add(newAccountToSaveInTheRealDatabase);
        await databaseContextOfThisChainTest.SaveChangesAsync();
        databaseContextOfThisChainTest.ChangeTracker.Clear();

        return newAccountToSaveInTheRealDatabase.Id;
    }

    // Fait une tentative de connexion comme le ferait une requête, avec son propre état en mémoire.
    private async Task<AccountLoginResult> TryToLogInLikeOneRealRequestAsync(WalletFlowDatabaseContext databaseContextOfThisChainTest, string emailAddressTypedInThisTest, string passwordTypedByTheVisitorInThisTest)
    {
        var serviceThatLogsTheVisitorIn = new AccountLoginService(
            new UserAccountRepository(databaseContextOfThisChainTest),
            new SecurityEventRepository(databaseContextOfThisChainTest),
            queueThatKeepsTheEmailRequestsInMemory,
            realHasherOfThePasswordsInTheseTests,
            realGeneratorOfTheTokensInTheseTests,
            new InMemoryLoginsWaitingForTheirCodeStore(clockThatOnlyMovesWhenTestsAskIt),
            new UserSessionOpeningService(
                new UserAccountRepository(databaseContextOfThisChainTest),
                new UserSessionRepository(databaseContextOfThisChainTest),
                new TwoFactorAuthenticationRepository(databaseContextOfThisChainTest),
                realGeneratorOfTheTokensInTheseTests,
                clockThatOnlyMovesWhenTestsAskIt),
            clockThatOnlyMovesWhenTestsAskIt);

        var resultOfTheLoginAttemptOfTheVisitor = await serviceThatLogsTheVisitorIn.LogVisitorInWithEmailAddressAndPasswordAsync(new LoginAttemptFromVisitor
        {
            EmailAddressTypedByTheVisitor = emailAddressTypedInThisTest,
            PasswordTextTypedByTheVisitor = passwordTypedByTheVisitorInThisTest,
            IpAddressOfTheVisitor = IpAddressOfTheVisitorInTheseTests
        });
        databaseContextOfThisChainTest.ChangeTracker.Clear();

        return resultOfTheLoginAttemptOfTheVisitor;
    }

    // Traite toutes les demandes de la file, comme le ferait la tâche de fond, et les retire de la file.
    private async Task ProcessEveryEmailRequestOfTheQueueAsync(WalletFlowDatabaseContext databaseContextOfThisChainTest)
    {
        foreach (var requestWaitingInTheBackgroundQueue in queueThatKeepsTheEmailRequestsInMemory.RequestsAddedToTheBackgroundQueue)
        {
            var serviceThatProcessesTheEmailRequest = new BackgroundEmailRequestProcessingService(
                new UserAccountRepository(databaseContextOfThisChainTest),
                new SecurityEventRepository(databaseContextOfThisChainTest),
                realGeneratorOfTheTokensInTheseTests,
                senderThatKeepsTheEmailsInMemory,
                new PublicWebsiteSettings("https://walletflow.test"),
                clockThatOnlyMovesWhenTestsAskIt);

            await serviceThatProcessesTheEmailRequest.ProcessOneBackgroundEmailRequestAsync(requestWaitingInTheBackgroundQueue);
            databaseContextOfThisChainTest.ChangeTracker.Clear();
        }

        queueThatKeepsTheEmailRequestsInMemory.RequestsAddedToTheBackgroundQueue.Clear();
    }

    // Demande un lien de récupération et renvoie le jeton lu dans l'e-mail, comme le ferait le visiteur.
    private async Task<string> RequestRecoveryThenReadTheTokenOfTheLinkAsync(WalletFlowDatabaseContext databaseContextOfThisChainTest, string emailAddressTypedInThisTest)
    {
        var serviceThatRequestsThePasswordRecovery = new PasswordRecoveryRequestService(new InMemoryPasswordRecoveryRequestLimiter(clockThatOnlyMovesWhenTestsAskIt), queueThatKeepsTheEmailRequestsInMemory);

        Assert.True(serviceThatRequestsThePasswordRecovery.RequestPasswordRecoveryForThisEmailAddress(emailAddressTypedInThisTest, IpAddressOfTheVisitorInTheseTests).IsAccepted);
        await ProcessEveryEmailRequestOfTheQueueAsync(databaseContextOfThisChainTest);

        return ReadTokenWrittenInTheConfirmationLink(senderThatKeepsTheEmailsInMemory.SentEmails.Last().Body);
    }

    // Envoie un nouveau mot de passe comme le ferait une requête.
    private async Task<PasswordResetResult> ResetPasswordLikeOneRealRequestAsync(WalletFlowDatabaseContext databaseContextOfThisChainTest, string tokenReadInTheRecoveryLink, string newPasswordTypedInThisTest)
    {
        var serviceThatChangesThePasswordOfTheAccount = new PasswordResetService(
            new UserAccountRepository(databaseContextOfThisChainTest),
            new UserSessionRepository(databaseContextOfThisChainTest),
            realHasherOfThePasswordsInTheseTests,
            realGeneratorOfTheTokensInTheseTests,
            clockThatOnlyMovesWhenTestsAskIt);

        var resultOfThePasswordResetRequest = await serviceThatChangesThePasswordOfTheAccount.ResetPasswordWithLinkTokenAsync(tokenReadInTheRecoveryLink, newPasswordTypedInThisTest, IpAddressOfTheVisitorInTheseTests);
        databaseContextOfThisChainTest.ChangeTracker.Clear();

        return resultOfThePasswordResetRequest;
    }

    // Clique sur le lien de déblocage comme le ferait une requête.
    private async Task<AccountUnlockResult> UnlockAccountLikeOneRealRequestAsync(WalletFlowDatabaseContext databaseContextOfThisChainTest, string tokenReadInTheUnlockLink)
    {
        var serviceThatUnlocksTheUserAccount = new AccountUnlockService(new UserAccountRepository(databaseContextOfThisChainTest), realGeneratorOfTheTokensInTheseTests, clockThatOnlyMovesWhenTestsAskIt);

        var resultOfTheAccountUnlockRequest = await serviceThatUnlocksTheUserAccount.UnlockAccountWithLinkTokenAsync(tokenReadInTheUnlockLink, IpAddressOfTheVisitorInTheseTests);
        databaseContextOfThisChainTest.ChangeTracker.Clear();

        return resultOfTheAccountUnlockRequest;
    }

    // Compte les événements de ce type et de ce résultat dans le vrai journal, pour ce compte.
    private static async Task<int> CountSecurityEventsOfThisAccountAsync(
        WalletFlowDatabaseContext databaseContextOfThisChainTest,
        Guid identifierOfTheAccountInTheJournal,
        SecurityEventType typeOfTheEventsToCount,
        SecurityEventResult resultOfTheEventsToCount)
    {
        return await databaseContextOfThisChainTest.SecurityEvents.CountAsync(eventSearchedInTheDatabaseTable =>
            eventSearchedInTheDatabaseTable.UserId == identifierOfTheAccountInTheJournal
            && eventSearchedInTheDatabaseTable.Type == typeOfTheEventsToCount
            && eventSearchedInTheDatabaseTable.Result == resultOfTheEventsToCount);
    }

    // Tout le parcours : session ouverte, lien reçu, nouveau mot de passe, session fermée, seul le nouveau mot de passe marche.
    [Fact]
    public async Task RecoveryChangesThePasswordClosesTheSessionsAndOnlyTheNewPasswordWorks()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var identifierOfTheAccountOfThisTest = await SaveAccountWithThisPasswordAsync(databaseContextOfThisChainTest, "chaine-recuperation@exemple.test", ValidPasswordChosenInTheseTests);
        var resultOfTheLoginBeforeTheRecovery = await TryToLogInLikeOneRealRequestAsync(databaseContextOfThisChainTest, "chaine-recuperation@exemple.test", ValidPasswordChosenInTheseTests);
        Assert.True(resultOfTheLoginBeforeTheRecovery.IsLoggedIn);

        var tokenReadInTheRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(databaseContextOfThisChainTest, "chaine-recuperation@exemple.test");
        var resultOfThePasswordResetRequest = await ResetPasswordLikeOneRealRequestAsync(databaseContextOfThisChainTest, tokenReadInTheRecoveryLink, NewPasswordChosenInTheseTests);

        Assert.True(resultOfThePasswordResetRequest.IsPasswordChanged);
        Assert.Equal(0, await databaseContextOfThisChainTest.UserSessions.CountAsync(sessionSearchedInTheDatabaseTable => sessionSearchedInTheDatabaseTable.UserId == identifierOfTheAccountOfThisTest));
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisChainTest, identifierOfTheAccountOfThisTest, SecurityEventType.PasswordRecovery, SecurityEventResult.Success));
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisChainTest, identifierOfTheAccountOfThisTest, SecurityEventType.PasswordChange, SecurityEventResult.Success));

        var resultOfTheLoginWithTheOldPassword = await TryToLogInLikeOneRealRequestAsync(databaseContextOfThisChainTest, "chaine-recuperation@exemple.test", ValidPasswordChosenInTheseTests);
        var resultOfTheLoginWithTheNewPassword = await TryToLogInLikeOneRealRequestAsync(databaseContextOfThisChainTest, "chaine-recuperation@exemple.test", NewPasswordChosenInTheseTests);
        Assert.False(resultOfTheLoginWithTheOldPassword.IsLoggedIn);
        Assert.True(resultOfTheLoginWithTheNewPassword.IsLoggedIn);

        var resultWithTheSameLinkAgain = await ResetPasswordLikeOneRealRequestAsync(databaseContextOfThisChainTest, tokenReadInTheRecoveryLink, "Encore-Un-Mot-De-Passe-3");
        Assert.Equal(PasswordResetService.ErrorMessageForRecoveryLinkNoLongerValid, resultWithTheSameLinkAgain.LinkErrorMessage);
    }

    // Le vrai Argon2 voit deux mots différents sans la normalisation : c'est le seul test qui le prouve de bout en bout.
    [Fact]
    public async Task NewPasswordTypedWithComposedAccentLogsInWithDecomposedAccent()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        await SaveAccountWithThisPasswordAsync(databaseContextOfThisChainTest, "chaine-recuperation-accent@exemple.test", ValidPasswordChosenInTheseTests);
        var tokenReadInTheRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(databaseContextOfThisChainTest, "chaine-recuperation-accent@exemple.test");

        await ResetPasswordLikeOneRealRequestAsync(databaseContextOfThisChainTest, tokenReadInTheRecoveryLink, "Mot-De-Passé-1");
        var resultOfTheLoginOnAnotherDevice = await TryToLogInLikeOneRealRequestAsync(databaseContextOfThisChainTest, "chaine-recuperation-accent@exemple.test", "Mot-De-Passé-1");

        Assert.True(resultOfTheLoginOnAnotherDevice.IsLoggedIn);
    }

    // GR Auth : une récupération réussie débloque aussi le compte.
    [Fact]
    public async Task RecoveryUnlocksTheAccountOnTheRealDatabase()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var identifierOfTheAccountOfThisTest = await SaveAccountWithThisPasswordAsync(databaseContextOfThisChainTest, "chaine-recuperation-blocage@exemple.test", ValidPasswordChosenInTheseTests);

        for (var numberOfTheWrongPasswordBeingTried = 1; numberOfTheWrongPasswordBeingTried <= 5; numberOfTheWrongPasswordBeingTried++)
        {
            await TryToLogInLikeOneRealRequestAsync(databaseContextOfThisChainTest, "chaine-recuperation-blocage@exemple.test", "Mauvais-Mot-De-Passe-2");
        }

        var tokenReadInTheRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(databaseContextOfThisChainTest, "chaine-recuperation-blocage@exemple.test");
        await ResetPasswordLikeOneRealRequestAsync(databaseContextOfThisChainTest, tokenReadInTheRecoveryLink, NewPasswordChosenInTheseTests);

        var accountReadAgainAfterTheRecovery = await databaseContextOfThisChainTest.Users.AsNoTracking().SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == identifierOfTheAccountOfThisTest);
        Assert.Equal(0, accountReadAgainAfterTheRecovery.FailedLoginAttemptCount);
        Assert.Null(accountReadAgainAfterTheRecovery.LockoutEndsAt);
        Assert.Equal(1, accountReadAgainAfterTheRecovery.LockoutCount);

        var resultOfTheLoginAfterTheRecovery = await TryToLogInLikeOneRealRequestAsync(databaseContextOfThisChainTest, "chaine-recuperation-blocage@exemple.test", NewPasswordChosenInTheseTests);
        Assert.True(resultOfTheLoginAfterTheRecovery.IsLoggedIn);
    }

    // Tout le parcours du déblocage : 5 essais, un seul e-mail, lien qui débloque sans connecter.
    [Fact]
    public async Task UnlockLinkReceivedAfterTheFifthAttemptOpensTheAccountWithoutLoggingIn()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var identifierOfTheAccountOfThisTest = await SaveAccountWithThisPasswordAsync(databaseContextOfThisChainTest, "chaine-deblocage@exemple.test", ValidPasswordChosenInTheseTests);

        for (var numberOfTheWrongPasswordBeingTried = 1; numberOfTheWrongPasswordBeingTried <= 7; numberOfTheWrongPasswordBeingTried++)
        {
            await TryToLogInLikeOneRealRequestAsync(databaseContextOfThisChainTest, "chaine-deblocage@exemple.test", "Mauvais-Mot-De-Passe-2");
        }

        await ProcessEveryEmailRequestOfTheQueueAsync(databaseContextOfThisChainTest);

        var emailReceivedByTheAddressOwner = Assert.Single(senderThatKeepsTheEmailsInMemory.SentEmails);
        Assert.Contains("https://walletflow.test/unlock-account#token=", emailReceivedByTheAddressOwner.Body);
        var tokenReadInTheUnlockLink = ReadTokenWrittenInTheConfirmationLink(emailReceivedByTheAddressOwner.Body);

        var resultOfTheAccountUnlockRequest = await UnlockAccountLikeOneRealRequestAsync(databaseContextOfThisChainTest, tokenReadInTheUnlockLink);

        Assert.True(resultOfTheAccountUnlockRequest.IsUnlocked);
        Assert.Equal(0, await databaseContextOfThisChainTest.UserSessions.CountAsync(sessionSearchedInTheDatabaseTable => sessionSearchedInTheDatabaseTable.UserId == identifierOfTheAccountOfThisTest));
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisChainTest, identifierOfTheAccountOfThisTest, SecurityEventType.Unlock, SecurityEventResult.Success));

        var accountReadAgainAfterTheUnlock = await databaseContextOfThisChainTest.Users.AsNoTracking().SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == identifierOfTheAccountOfThisTest);
        Assert.Equal(0, accountReadAgainAfterTheUnlock.FailedLoginAttemptCount);
        Assert.Null(accountReadAgainAfterTheUnlock.LockoutEndsAt);
        Assert.Equal(1, accountReadAgainAfterTheUnlock.LockoutCount);

        var resultOfTheLoginAfterTheUnlock = await TryToLogInLikeOneRealRequestAsync(databaseContextOfThisChainTest, "chaine-deblocage@exemple.test", ValidPasswordChosenInTheseTests);
        Assert.True(resultOfTheLoginAfterTheUnlock.IsLoggedIn);

        var resultWithTheSameLinkAgain = await UnlockAccountLikeOneRealRequestAsync(databaseContextOfThisChainTest, tokenReadInTheUnlockLink);
        Assert.Equal(AccountUnlockService.ErrorMessageForUnlockLinkNoLongerValid, resultWithTheSameLinkAgain.LinkErrorMessage);
    }

    [Fact]
    public async Task SecondRecoveryLinkReplacesTheFirstOneOnTheRealDatabase()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        await SaveAccountWithThisPasswordAsync(databaseContextOfThisChainTest, "chaine-deux-liens@exemple.test", ValidPasswordChosenInTheseTests);

        var tokenOfTheFirstRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(databaseContextOfThisChainTest, "chaine-deux-liens@exemple.test");
        var tokenOfTheSecondRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(databaseContextOfThisChainTest, "chaine-deux-liens@exemple.test");

        var resultWithTheFirstRecoveryLink = await ResetPasswordLikeOneRealRequestAsync(databaseContextOfThisChainTest, tokenOfTheFirstRecoveryLink, NewPasswordChosenInTheseTests);
        var resultWithTheSecondRecoveryLink = await ResetPasswordLikeOneRealRequestAsync(databaseContextOfThisChainTest, tokenOfTheSecondRecoveryLink, NewPasswordChosenInTheseTests);

        Assert.Equal(PasswordResetService.ErrorMessageForRecoveryLinkNoLongerValid, resultWithTheFirstRecoveryLink.LinkErrorMessage);
        Assert.True(resultWithTheSecondRecoveryLink.IsPasswordChanged);
    }

    // Un lien de récupération arrivé à 30 minutes est refusé sur la vraie base, à la seconde près.
    [Fact]
    public async Task RecoveryLinkExpiresAfterThirtyMinutesOnTheRealDatabase()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        await SaveAccountWithThisPasswordAsync(databaseContextOfThisChainTest, "chaine-lien-expire@exemple.test", ValidPasswordChosenInTheseTests);
        var tokenReadInTheRecoveryLink = await RequestRecoveryThenReadTheTokenOfTheLinkAsync(databaseContextOfThisChainTest, "chaine-lien-expire@exemple.test");

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(30));
        var resultOfTheLatePasswordReset = await ResetPasswordLikeOneRealRequestAsync(databaseContextOfThisChainTest, tokenReadInTheRecoveryLink, NewPasswordChosenInTheseTests);

        Assert.Equal(PasswordResetService.ErrorMessageForRecoveryLinkNoLongerValid, resultOfTheLatePasswordReset.LinkErrorMessage);
    }

    [Fact]
    public async Task UnconfirmedAddressGetsNoLinkOnTheRealDatabase()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var identifierOfTheAccountOfThisTest = await SaveAccountWithThisPasswordAsync(databaseContextOfThisChainTest, "chaine-non-confirmee@exemple.test", ValidPasswordChosenInTheseTests, emailAddressOfTheAccountIsConfirmed: false);
        var serviceThatRequestsThePasswordRecovery = new PasswordRecoveryRequestService(new InMemoryPasswordRecoveryRequestLimiter(clockThatOnlyMovesWhenTestsAskIt), queueThatKeepsTheEmailRequestsInMemory);

        serviceThatRequestsThePasswordRecovery.RequestPasswordRecoveryForThisEmailAddress("chaine-non-confirmee@exemple.test", IpAddressOfTheVisitorInTheseTests);
        await ProcessEveryEmailRequestOfTheQueueAsync(databaseContextOfThisChainTest);

        Assert.DoesNotContain("#token=", Assert.Single(senderThatKeepsTheEmailsInMemory.SentEmails).Body);
        Assert.Equal(0, await databaseContextOfThisChainTest.EmailTokens.CountAsync(tokenSearchedInTheDatabaseTable => tokenSearchedInTheDatabaseTable.UserId == identifierOfTheAccountOfThisTest));
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisChainTest, identifierOfTheAccountOfThisTest, SecurityEventType.PasswordRecovery, SecurityEventResult.Failure));
    }

    // Une adresse inconnue laisse une trace sans identifiant, avec l'adresse IP, et l'adresse tapée n'est écrite nulle part (SS §7).
    [Fact]
    public async Task UnknownAddressIsWrittenInTheRealJournalWithoutIdentifier()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var numberOfEventsWithoutIdentifierBeforeTheRequest = await databaseContextOfThisChainTest.SecurityEvents.CountAsync(eventSearchedInTheDatabaseTable =>
            eventSearchedInTheDatabaseTable.UserId == null && eventSearchedInTheDatabaseTable.Type == SecurityEventType.PasswordRecovery);
        var serviceThatRequestsThePasswordRecovery = new PasswordRecoveryRequestService(new InMemoryPasswordRecoveryRequestLimiter(clockThatOnlyMovesWhenTestsAskIt), queueThatKeepsTheEmailRequestsInMemory);

        serviceThatRequestsThePasswordRecovery.RequestPasswordRecoveryForThisEmailAddress("adresse-sans-compte@exemple.test", IpAddressOfTheVisitorInTheseTests);
        await ProcessEveryEmailRequestOfTheQueueAsync(databaseContextOfThisChainTest);

        Assert.Empty(senderThatKeepsTheEmailsInMemory.SentEmails);
        Assert.Equal(numberOfEventsWithoutIdentifierBeforeTheRequest + 1, await databaseContextOfThisChainTest.SecurityEvents.CountAsync(eventSearchedInTheDatabaseTable =>
            eventSearchedInTheDatabaseTable.UserId == null && eventSearchedInTheDatabaseTable.Type == SecurityEventType.PasswordRecovery));
    }
}
