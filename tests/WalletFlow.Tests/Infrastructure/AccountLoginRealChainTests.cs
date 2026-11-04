using System.Net;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.Infrastructure.LocalDatabaseForTheTests;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la connexion avec les vrais outils : Argon2, jetons et base PostgreSQL locale.
// Seuls l'envoi des e-mails et l'horloge sont remplacés. Chaque test annule sa transaction à la fin : la base reste vide.
public class AccountLoginRealChainTests
{
    private const string WrongPasswordTypedInTheseTests = "Mauvais-Mot-De-Passe-2";

    private static readonly IPAddress IpAddressOfTheVisitorInTheseTests = IPAddress.Parse("203.0.113.7");

    private readonly LibsodiumPasswordHasher realHasherOfThePasswordsInTheseTests = new LibsodiumPasswordHasher();
    private readonly RandomSecureTokenGenerator realGeneratorOfTheTokensInTheseTests = new RandomSecureTokenGenerator();
    private readonly FakeEmailSender senderThatKeepsTheEmailsInMemory = new FakeEmailSender();
    private readonly FakeTimeProviderForTests clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();

    // Inscrit puis confirme un compte, comme le feraient deux requêtes.
    private async Task<Guid> RegisterThenConfirmNewUserAccountAsync(WalletFlowDatabaseContext databaseContextOfThisChainTest, string emailAddressOfTheNewAccount, string passwordChosenByTheVisitorAtRegistration)
    {
        var identifierOfTheNewUserAccount = await RegisterAccountWithoutConfirmingItAsync(databaseContextOfThisChainTest, emailAddressOfTheNewAccount, passwordChosenByTheVisitorAtRegistration);

        var serviceThatConfirmsTheNewAccount = new AccountConfirmationService(
            new UserAccountRepository(databaseContextOfThisChainTest),
            realHasherOfThePasswordsInTheseTests,
            realGeneratorOfTheTokensInTheseTests,
            clockThatOnlyMovesWhenTestsAskIt);
        var resultOfTheConfirmationOfTheAccount = await serviceThatConfirmsTheNewAccount.ConfirmAccountWithLinkTokenAndPasswordAsync(
            ReadTokenWrittenInTheConfirmationLink(senderThatKeepsTheEmailsInMemory.SentEmails.Last().Body),
            passwordChosenByTheVisitorAtRegistration);
        Assert.True(resultOfTheConfirmationOfTheAccount.IsConfirmed);
        databaseContextOfThisChainTest.ChangeTracker.Clear();

        return identifierOfTheNewUserAccount;
    }

    // Inscrit un compte sans le confirmer, comme le ferait une requête.
    private async Task<Guid> RegisterAccountWithoutConfirmingItAsync(WalletFlowDatabaseContext databaseContextOfThisChainTest, string emailAddressOfTheNewAccount, string passwordChosenByTheVisitorAtRegistration)
    {
        var serviceThatRegistersTheNewAccount = new AccountRegistrationService(
            new UserAccountRepository(databaseContextOfThisChainTest),
            realHasherOfThePasswordsInTheseTests,
            realGeneratorOfTheTokensInTheseTests,
            senderThatKeepsTheEmailsInMemory,
            new PublicWebsiteSettings("https://walletflow.test"),
            new InMemoryEmailAddressRequestLimiter(clockThatOnlyMovesWhenTestsAskIt),
            clockThatOnlyMovesWhenTestsAskIt);
        var resultOfTheRegistrationOfTheAccount = await serviceThatRegistersTheNewAccount.RegisterNewAccountForVisitorAsync(emailAddressOfTheNewAccount, passwordChosenByTheVisitorAtRegistration);
        Assert.True(resultOfTheRegistrationOfTheAccount.IsAccepted);
        databaseContextOfThisChainTest.ChangeTracker.Clear();

        var accountReadBackFromTheRealDatabase = await databaseContextOfThisChainTest.Users.AsNoTracking().SingleAsync(userSearchedInTheDatabaseTable =>
            userSearchedInTheDatabaseTable.Email == emailAddressOfTheNewAccount);

        return accountReadBackFromTheRealDatabase.Id;
    }

    // Fait une tentative de connexion comme le ferait une requête, avec son propre état en mémoire.
    private async Task<AccountLoginResult> TryToLogInLikeOneRealRequestAsync(WalletFlowDatabaseContext databaseContextOfThisChainTest, string emailAddressTypedInThisTest, string passwordTypedByTheVisitorInThisTest)
    {
        var serviceThatLogsTheVisitorIn = new AccountLoginService(
            new UserAccountRepository(databaseContextOfThisChainTest),
            new UserSessionRepository(databaseContextOfThisChainTest),
            new SecurityEventRepository(databaseContextOfThisChainTest),
            realHasherOfThePasswordsInTheseTests,
            realGeneratorOfTheTokensInTheseTests,
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

    // Crée le service qui reconnaît les sessions, sur cet accès à la base.
    private UserSessionService CreateRealSessionServiceOnThisDatabaseContext(WalletFlowDatabaseContext databaseContextOfThisChainTest)
    {
        return new UserSessionService(new UserSessionRepository(databaseContextOfThisChainTest), realGeneratorOfTheTokensInTheseTests, clockThatOnlyMovesWhenTestsAskIt);
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

    // Tout le parcours : inscription, confirmation, connexion, session reconnue, déconnexion.
    [Fact]
    public async Task SignupConfirmationLoginAndLogoutWorkTogetherOnTheRealDatabase()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var identifierOfTheNewUserAccount = await RegisterThenConfirmNewUserAccountAsync(databaseContextOfThisChainTest, "chaine-connexion@exemple.test", ValidPasswordChosenInTheseTests);

        var resultOfTheLoginOfTheAccount = await TryToLogInLikeOneRealRequestAsync(databaseContextOfThisChainTest, "chaine-connexion@exemple.test", ValidPasswordChosenInTheseTests);

        Assert.True(resultOfTheLoginOfTheAccount.IsLoggedIn);
        var sessionReadBackFromTheRealDatabase = await databaseContextOfThisChainTest.UserSessions.AsNoTracking().SingleAsync(sessionSearchedInTheDatabaseTable =>
            sessionSearchedInTheDatabaseTable.UserId == identifierOfTheNewUserAccount);
        Assert.NotEqual(resultOfTheLoginOfTheAccount.SessionTokenToPutInTheCookie, sessionReadBackFromTheRealDatabase.Id);
        Assert.Equal(64, sessionReadBackFromTheRealDatabase.Id.Length);
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisChainTest, identifierOfTheNewUserAccount, SecurityEventType.Login, SecurityEventResult.Success));
        Assert.Equal(1, await databaseContextOfThisChainTest.Devices.CountAsync(deviceSearchedInTheDatabaseTable => deviceSearchedInTheDatabaseTable.UserId == identifierOfTheNewUserAccount));

        var serviceOfTheSessionsBeingTested = CreateRealSessionServiceOnThisDatabaseContext(databaseContextOfThisChainTest);
        Assert.Equal(identifierOfTheNewUserAccount, await serviceOfTheSessionsBeingTested.FindOwnerOfTheActiveSessionThenRecordItsActivityAsync(resultOfTheLoginOfTheAccount.SessionTokenToPutInTheCookie!));

        await serviceOfTheSessionsBeingTested.CloseUserSessionOfThisCookieAsync(resultOfTheLoginOfTheAccount.SessionTokenToPutInTheCookie!);

        Assert.Null(await serviceOfTheSessionsBeingTested.FindOwnerOfTheActiveSessionThenRecordItsActivityAsync(resultOfTheLoginOfTheAccount.SessionTokenToPutInTheCookie!));
    }

    // Le vrai Argon2 voit deux mots différents sans la normalisation : c'est le seul test qui le prouve de bout en bout.
    [Fact]
    public async Task PasswordTypedWithDecomposedAccentLogsInWithTheRealHasher()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        await RegisterThenConfirmNewUserAccountAsync(databaseContextOfThisChainTest, "chaine-accent@exemple.test", "Mot-De-Passé-1");

        var resultOfTheLoginOfTheAccount = await TryToLogInLikeOneRealRequestAsync(databaseContextOfThisChainTest, "chaine-accent@exemple.test", "Mot-De-Passé-1");

        Assert.True(resultOfTheLoginOfTheAccount.IsLoggedIn);
    }

    [Fact]
    public async Task FiveWrongPasswordsReallyLockTheAccountThenItOpensAgainAfterTheLockout()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var identifierOfTheNewUserAccount = await RegisterThenConfirmNewUserAccountAsync(databaseContextOfThisChainTest, "chaine-blocage@exemple.test", ValidPasswordChosenInTheseTests);

        for (var numberOfTheWrongPasswordBeingTried = 1; numberOfTheWrongPasswordBeingTried <= 5; numberOfTheWrongPasswordBeingTried++)
        {
            var resultOfTheWrongPasswordAttempt = await TryToLogInLikeOneRealRequestAsync(databaseContextOfThisChainTest, "chaine-blocage@exemple.test", WrongPasswordTypedInTheseTests);
            Assert.False(resultOfTheWrongPasswordAttempt.IsLoggedIn);
        }

        var accountReadAgainAfterTheLockout = await databaseContextOfThisChainTest.Users.AsNoTracking().SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == identifierOfTheNewUserAccount);
        Assert.Equal(5, accountReadAgainAfterTheLockout.FailedLoginAttemptCount);
        Assert.Equal(1, accountReadAgainAfterTheLockout.LockoutCount);
        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddMinutes(15), accountReadAgainAfterTheLockout.LockoutEndsAt);

        var resultDuringTheLockoutOfTheAccount = await TryToLogInLikeOneRealRequestAsync(databaseContextOfThisChainTest, "chaine-blocage@exemple.test", ValidPasswordChosenInTheseTests);
        Assert.Equal(AccountLoginService.ErrorMessageForWrongLoginCredentials, resultDuringTheLockoutOfTheAccount.WrongCredentialsErrorMessage);

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(15));
        var resultAfterTheEndOfTheLockout = await TryToLogInLikeOneRealRequestAsync(databaseContextOfThisChainTest, "chaine-blocage@exemple.test", ValidPasswordChosenInTheseTests);
        Assert.True(resultAfterTheEndOfTheLockout.IsLoggedIn);

        var accountReadAgainAfterTheLogin = await databaseContextOfThisChainTest.Users.AsNoTracking().SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == identifierOfTheNewUserAccount);
        Assert.Equal(0, accountReadAgainAfterTheLogin.FailedLoginAttemptCount);
        Assert.Equal(0, accountReadAgainAfterTheLogin.LockoutCount);
        Assert.Null(accountReadAgainAfterTheLogin.LockoutEndsAt);

        Assert.Equal(6, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisChainTest, identifierOfTheNewUserAccount, SecurityEventType.Login, SecurityEventResult.Failure));
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisChainTest, identifierOfTheNewUserAccount, SecurityEventType.Lockout, SecurityEventResult.Success));
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisChainTest, identifierOfTheNewUserAccount, SecurityEventType.Login, SecurityEventResult.Success));
    }

    [Fact]
    public async Task SessionExpiresAfterThirtyMinutesOnTheRealDatabaseAndActivityExtendsIt()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var identifierOfTheNewUserAccount = await RegisterThenConfirmNewUserAccountAsync(databaseContextOfThisChainTest, "chaine-expiration@exemple.test", ValidPasswordChosenInTheseTests);
        var resultOfTheLoginOfTheAccount = await TryToLogInLikeOneRealRequestAsync(databaseContextOfThisChainTest, "chaine-expiration@exemple.test", ValidPasswordChosenInTheseTests);
        var serviceOfTheSessionsBeingTested = CreateRealSessionServiceOnThisDatabaseContext(databaseContextOfThisChainTest);

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(29));
        Assert.Equal(identifierOfTheNewUserAccount, await serviceOfTheSessionsBeingTested.FindOwnerOfTheActiveSessionThenRecordItsActivityAsync(resultOfTheLoginOfTheAccount.SessionTokenToPutInTheCookie!));

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(29));
        Assert.Equal(identifierOfTheNewUserAccount, await serviceOfTheSessionsBeingTested.FindOwnerOfTheActiveSessionThenRecordItsActivityAsync(resultOfTheLoginOfTheAccount.SessionTokenToPutInTheCookie!));

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(30));
        Assert.Null(await serviceOfTheSessionsBeingTested.FindOwnerOfTheActiveSessionThenRecordItsActivityAsync(resultOfTheLoginOfTheAccount.SessionTokenToPutInTheCookie!));
    }

    [Fact]
    public async Task UnconfirmedAccountIsInvitedToConfirmOnTheRealDatabase()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var identifierOfTheNewUserAccount = await RegisterAccountWithoutConfirmingItAsync(databaseContextOfThisChainTest, "chaine-non-confirme@exemple.test", ValidPasswordChosenInTheseTests);

        var resultOfTheLoginOfTheAccount = await TryToLogInLikeOneRealRequestAsync(databaseContextOfThisChainTest, "chaine-non-confirme@exemple.test", ValidPasswordChosenInTheseTests);

        Assert.Equal(AccountLoginService.ErrorMessageForEmailAddressNotConfirmed, resultOfTheLoginOfTheAccount.EmailAddressNotConfirmedErrorMessage);
        Assert.Equal(0, await databaseContextOfThisChainTest.UserSessions.CountAsync(sessionSearchedInTheDatabaseTable => sessionSearchedInTheDatabaseTable.UserId == identifierOfTheNewUserAccount));
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisChainTest, identifierOfTheNewUserAccount, SecurityEventType.Login, SecurityEventResult.Failure));
    }

    // Une adresse inconnue laisse une trace sans identifiant, et l'adresse tapée n'est écrite nulle part (SS §7).
    [Fact]
    public async Task UnknownEmailAddressIsWrittenInTheRealJournalWithoutIdentifier()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var numberOfEventsWithoutIdentifierBeforeTheLogin = await databaseContextOfThisChainTest.SecurityEvents.CountAsync(eventSearchedInTheDatabaseTable => eventSearchedInTheDatabaseTable.UserId == null);

        var resultOfTheLoginOfTheAccount = await TryToLogInLikeOneRealRequestAsync(databaseContextOfThisChainTest, "adresse-sans-compte@exemple.test", ValidPasswordChosenInTheseTests);

        Assert.Equal(AccountLoginService.ErrorMessageForWrongLoginCredentials, resultOfTheLoginOfTheAccount.WrongCredentialsErrorMessage);
        Assert.Equal(numberOfEventsWithoutIdentifierBeforeTheLogin + 1, await databaseContextOfThisChainTest.SecurityEvents.CountAsync(eventSearchedInTheDatabaseTable => eventSearchedInTheDatabaseTable.UserId == null));
    }

    // PostgreSQL refuse le caractère nul : sans le contrôle du format, ce visiteur recevrait une erreur 500.
    [Fact]
    public async Task EmailAddressWithNullCharacterGetsTheNormalRefusalOnTheRealDatabase()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();

        var resultOfTheLoginOfTheAccount = await TryToLogInLikeOneRealRequestAsync(databaseContextOfThisChainTest, "nul\0caractere@exemple.test", ValidPasswordChosenInTheseTests);

        Assert.Equal(AccountLoginService.ErrorMessageForWrongLoginCredentials, resultOfTheLoginOfTheAccount.WrongCredentialsErrorMessage);
    }
}
