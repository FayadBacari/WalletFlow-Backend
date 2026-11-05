using System.Net;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.TwoFactorAuthentication;
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

// Vérifie la double authentification avec les vrais outils : Argon2, Otp.NET, AES-GCM, jetons et base PostgreSQL locale.
// Seuls l'envoi des e-mails et l'horloge sont remplacés. Chaque test annule sa transaction à la fin : la base reste vide.
public class TwoFactorRealChainTests
{
    private const string EmailAddressOfTheAccountInTheseTests = "chaine-double-auth@exemple.test";

    private static readonly IPAddress IpAddressOfTheVisitorInTheseTests = IPAddress.Parse("203.0.113.7");

    private readonly LibsodiumPasswordHasher realHasherOfThePasswordsInTheseTests = new LibsodiumPasswordHasher();
    private readonly RandomSecureTokenGenerator realGeneratorOfTheTokensInTheseTests = new RandomSecureTokenGenerator();
    private readonly OtpNetAuthenticatorCodeTool realToolThatCalculatesTheCodes = new OtpNetAuthenticatorCodeTool();
    private readonly AesGcmTotpSecretProtector realProtectorOfTheSecretsInTheseTests = new AesGcmTotpSecretProtector(TwoFactorTestTools.CreateNewEncryptionKeyForOneTest());
    private readonly FakeEmailSender senderThatKeepsTheEmailsInMemory = new FakeEmailSender();
    private readonly FakeBackgroundEmailRequestQueue queueThatKeepsTheEmailRequestsInMemory = new FakeBackgroundEmailRequestQueue();
    private readonly FakeTimeProviderForTests clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();
    private readonly InMemoryLoginsWaitingForTheirCodeStore storeOfTheLoginsWaitingForTheirCode;
    private readonly InMemorySecretsWaitingForActivationStore storeOfTheSecretsWaitingForActivation;
    private readonly InMemoryUsedAuthenticatorCodesRegister registerOfTheCodesAlreadyUsed;

    // Donne aux outils en mémoire la fausse horloge. Ils survivent aux requêtes, comme dans la vraie API.
    public TwoFactorRealChainTests()
    {
        storeOfTheLoginsWaitingForTheirCode = new InMemoryLoginsWaitingForTheirCodeStore(clockThatOnlyMovesWhenTestsAskIt);
        storeOfTheSecretsWaitingForActivation = new InMemorySecretsWaitingForActivationStore(clockThatOnlyMovesWhenTestsAskIt);
        registerOfTheCodesAlreadyUsed = new InMemoryUsedAuthenticatorCodesRegister(clockThatOnlyMovesWhenTestsAskIt);
    }

    // Inscrit puis confirme un compte, comme le feraient deux requêtes, et renvoie son identifiant.
    private async Task<Guid> RegisterThenConfirmNewUserAccountAsync(WalletFlowDatabaseContext databaseContextOfThisChainTest)
    {
        var serviceThatRegistersTheNewAccount = new AccountRegistrationService(
            new UserAccountRepository(databaseContextOfThisChainTest),
            realHasherOfThePasswordsInTheseTests,
            realGeneratorOfTheTokensInTheseTests,
            senderThatKeepsTheEmailsInMemory,
            new PublicWebsiteSettings("https://walletflow.test"),
            new InMemoryEmailAddressRequestLimiter(clockThatOnlyMovesWhenTestsAskIt),
            clockThatOnlyMovesWhenTestsAskIt);
        await serviceThatRegistersTheNewAccount.RegisterNewAccountForVisitorAsync(EmailAddressOfTheAccountInTheseTests, ValidPasswordChosenInTheseTests);
        databaseContextOfThisChainTest.ChangeTracker.Clear();

        var serviceThatConfirmsTheNewAccount = new AccountConfirmationService(
            new UserAccountRepository(databaseContextOfThisChainTest),
            realHasherOfThePasswordsInTheseTests,
            realGeneratorOfTheTokensInTheseTests,
            clockThatOnlyMovesWhenTestsAskIt);
        var resultOfTheConfirmationOfTheAccount = await serviceThatConfirmsTheNewAccount.ConfirmAccountWithLinkTokenAndPasswordAsync(
            ReadTokenWrittenInTheConfirmationLink(senderThatKeepsTheEmailsInMemory.SentEmails.Last().Body),
            ValidPasswordChosenInTheseTests);
        Assert.True(resultOfTheConfirmationOfTheAccount.IsConfirmed);
        databaseContextOfThisChainTest.ChangeTracker.Clear();

        var accountReadBackFromTheRealDatabase = await databaseContextOfThisChainTest.Users.AsNoTracking().SingleAsync(userSearchedInTheDatabaseTable =>
            userSearchedInTheDatabaseTable.Email == EmailAddressOfTheAccountInTheseTests);

        return accountReadBackFromTheRealDatabase.Id;
    }

    // Crée le service qui vérifie les codes, sur cet accès à la base.
    private TwoFactorCodeCheckingService CreateRealCodeCheckingServiceOnThisContext(WalletFlowDatabaseContext databaseContextOfThisChainTest)
    {
        return new TwoFactorCodeCheckingService(
            new TwoFactorAuthenticationRepository(databaseContextOfThisChainTest),
            realHasherOfThePasswordsInTheseTests,
            realToolThatCalculatesTheCodes,
            realProtectorOfTheSecretsInTheseTests,
            registerOfTheCodesAlreadyUsed,
            clockThatOnlyMovesWhenTestsAskIt);
    }

    // Crée le service qui ouvre les sessions, sur cet accès à la base.
    private UserSessionOpeningService CreateRealSessionOpeningServiceOnThisContext(WalletFlowDatabaseContext databaseContextOfThisChainTest)
    {
        return new UserSessionOpeningService(
            new UserAccountRepository(databaseContextOfThisChainTest),
            new UserSessionRepository(databaseContextOfThisChainTest),
            new TwoFactorAuthenticationRepository(databaseContextOfThisChainTest),
            realGeneratorOfTheTokensInTheseTests,
            clockThatOnlyMovesWhenTestsAskIt);
    }

    // Active la double authentification comme le feraient deux requêtes. Renvoie le secret du téléphone et les codes de secours.
    private async Task<(string SecretOfThePhone, List<string> BackupCodesOfTheUser)> ActivateTwoFactorLikeTwoRealRequestsAsync(WalletFlowDatabaseContext databaseContextOfThisChainTest, Guid identifierOfTheUserAccountInTheseTests)
    {
        var resultOfTheStartOfTheActivation = await CreateRealTwoFactorActivationServiceOnThisContext(databaseContextOfThisChainTest).StartActivationWithThePasswordOfTheUserAsync(identifierOfTheUserAccountInTheseTests, ValidPasswordChosenInTheseTests, IpAddressOfTheVisitorInTheseTests);
        databaseContextOfThisChainTest.ChangeTracker.Clear();
        var secretSharedWithThePhoneOfTheUser = resultOfTheStartOfTheActivation.SecretOfTheAuthenticatorApplication!;

        var resultOfTheEndOfTheActivation = await CreateRealTwoFactorActivationServiceOnThisContext(databaseContextOfThisChainTest).FinishActivationWithTheFirstCodeOfTheUserAsync(identifierOfTheUserAccountInTheseTests, CalculateCodeShownOnThePhoneNow(secretSharedWithThePhoneOfTheUser), IpAddressOfTheVisitorInTheseTests);
        databaseContextOfThisChainTest.ChangeTracker.Clear();
        Assert.True(resultOfTheEndOfTheActivation.IsTwoFactorActivated);

        return (secretSharedWithThePhoneOfTheUser, resultOfTheEndOfTheActivation.BackupCodesToGiveOnlyOnce!);
    }

    // Crée le service qui active la double authentification, sur cet accès à la base.
    private TwoFactorActivationService CreateRealTwoFactorActivationServiceOnThisContext(WalletFlowDatabaseContext databaseContextOfThisChainTest)
    {
        return new TwoFactorActivationService(
            new UserAccountRepository(databaseContextOfThisChainTest),
            new TwoFactorAuthenticationRepository(databaseContextOfThisChainTest),
            new SecurityEventRepository(databaseContextOfThisChainTest),
            realHasherOfThePasswordsInTheseTests,
            realGeneratorOfTheTokensInTheseTests,
            realToolThatCalculatesTheCodes,
            realProtectorOfTheSecretsInTheseTests,
            storeOfTheSecretsWaitingForActivation,
            CreateRealCodeCheckingServiceOnThisContext(databaseContextOfThisChainTest),
            clockThatOnlyMovesWhenTestsAskIt);
    }

    // Désactive comme le ferait une requête.
    private async Task<TwoFactorDeactivationResult> DeactivateLikeOneRealRequestAsync(WalletFlowDatabaseContext databaseContextOfThisChainTest, Guid identifierOfTheUserAccountInTheseTests, string codeTypedByTheUserInThisTest)
    {
        var serviceThatDeactivatesTheTwoFactor = new TwoFactorDeactivationService(
            new UserAccountRepository(databaseContextOfThisChainTest),
            new TwoFactorAuthenticationRepository(databaseContextOfThisChainTest),
            new SecurityEventRepository(databaseContextOfThisChainTest),
            realHasherOfThePasswordsInTheseTests,
            CreateRealCodeCheckingServiceOnThisContext(databaseContextOfThisChainTest),
            clockThatOnlyMovesWhenTestsAskIt);
        var resultOfTheDeactivationOfTheTwoFactor = await serviceThatDeactivatesTheTwoFactor.DeactivateTwoFactorWithPasswordAndCodeOfTheUserAsync(identifierOfTheUserAccountInTheseTests, ValidPasswordChosenInTheseTests, codeTypedByTheUserInThisTest, IpAddressOfTheVisitorInTheseTests);
        databaseContextOfThisChainTest.ChangeTracker.Clear();

        return resultOfTheDeactivationOfTheTwoFactor;
    }

    // Donne le mot de passe comme le ferait une requête.
    private async Task<AccountLoginResult> LogInWithThePasswordLikeOneRealRequestAsync(WalletFlowDatabaseContext databaseContextOfThisChainTest, string passwordTypedByTheVisitorInThisTest = ValidPasswordChosenInTheseTests)
    {
        var serviceThatLogsTheVisitorIn = new AccountLoginService(
            new UserAccountRepository(databaseContextOfThisChainTest),
            new SecurityEventRepository(databaseContextOfThisChainTest),
            queueThatKeepsTheEmailRequestsInMemory,
            realHasherOfThePasswordsInTheseTests,
            realGeneratorOfTheTokensInTheseTests,
            storeOfTheLoginsWaitingForTheirCode,
            CreateRealSessionOpeningServiceOnThisContext(databaseContextOfThisChainTest),
            clockThatOnlyMovesWhenTestsAskIt);
        var resultOfTheLoginAttemptOfTheVisitor = await serviceThatLogsTheVisitorIn.LogVisitorInWithEmailAddressAndPasswordAsync(new LoginAttemptFromVisitor
        {
            EmailAddressTypedByTheVisitor = EmailAddressOfTheAccountInTheseTests,
            PasswordTextTypedByTheVisitor = passwordTypedByTheVisitorInThisTest,
            IpAddressOfTheVisitor = IpAddressOfTheVisitorInTheseTests
        });
        databaseContextOfThisChainTest.ChangeTracker.Clear();

        return resultOfTheLoginAttemptOfTheVisitor;
    }

    // Finit la connexion comme le ferait une requête.
    private async Task<TwoFactorLoginResult> FinishLoginLikeOneRealRequestAsync(WalletFlowDatabaseContext databaseContextOfThisChainTest, string waitingLoginTokenOfTheVisitor, string codeTypedByTheVisitorInThisTest)
    {
        var serviceThatFinishesTheLoginWithACode = new TwoFactorLoginService(
            new UserAccountRepository(databaseContextOfThisChainTest),
            new SecurityEventRepository(databaseContextOfThisChainTest),
            realGeneratorOfTheTokensInTheseTests,
            storeOfTheLoginsWaitingForTheirCode,
            CreateRealCodeCheckingServiceOnThisContext(databaseContextOfThisChainTest),
            CreateRealSessionOpeningServiceOnThisContext(databaseContextOfThisChainTest),
            clockThatOnlyMovesWhenTestsAskIt);
        var resultOfTheCodeAttemptOfTheVisitor = await serviceThatFinishesTheLoginWithACode.FinishLoginOfTheVisitorWithThisCodeAsync(new TwoFactorCodeAttemptFromVisitor
        {
            CodeTypedByTheVisitor = codeTypedByTheVisitorInThisTest,
            IpAddressOfTheVisitor = IpAddressOfTheVisitorInTheseTests,
            WaitingLoginTokenReadInTheCookie = waitingLoginTokenOfTheVisitor
        });
        databaseContextOfThisChainTest.ChangeTracker.Clear();

        return resultOfTheCodeAttemptOfTheVisitor;
    }

    // Calcule le code qu'affiche le téléphone à la date de la fausse horloge.
    private string CalculateCodeShownOnThePhoneNow(string secretSharedWithThePhoneOfTheUser)
    {
        return TwoFactorTestTools.CalculateAuthenticatorCodeShownAtThisDate(secretSharedWithThePhoneOfTheUser, clockThatOnlyMovesWhenTestsAskIt.GetUtcNow());
    }

    // Passe à la période suivante : le téléphone affiche un nouveau code.
    private void WaitForTheNextCodeOnThePhone()
    {
        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromSeconds(TwoFactorTestTools.NumberOfSecondsInOneAuthenticatorPeriod));
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

    // Relit le compte dans la vraie base, sans passer par les objets en mémoire.
    private static async Task<User> ReadAccountBackFromTheRealDatabaseAsync(WalletFlowDatabaseContext databaseContextOfThisChainTest, Guid identifierOfTheUserAccountInTheseTests)
    {
        return await databaseContextOfThisChainTest.Users.AsNoTracking().SingleAsync(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == identifierOfTheUserAccountInTheseTests);
    }

    // Tout le début du parcours : activer, puis se reconnecter avec un vrai code calculé par une vraie bibliothèque.
    [Fact]
    public async Task ActivationThenLoginWithARealCodeWorkOnTheRealDatabase()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var identifierOfTheUserAccountInTheseTests = await RegisterThenConfirmNewUserAccountAsync(databaseContextOfThisChainTest);

        var (secretSharedWithThePhoneOfTheUser, backupCodesGivenToTheUserAtActivation) = await ActivateTwoFactorLikeTwoRealRequestsAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests);

        var accountReadAgainAfterTheActivation = await ReadAccountBackFromTheRealDatabaseAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests);
        Assert.True(accountReadAgainAfterTheActivation.IsTwoFactorEnabled);
        Assert.DoesNotContain(secretSharedWithThePhoneOfTheUser, accountReadAgainAfterTheActivation.EncryptedTotpSecret);
        Assert.Equal(secretSharedWithThePhoneOfTheUser, realProtectorOfTheSecretsInTheseTests.DecryptSecretOfThisUserAccount(accountReadAgainAfterTheActivation.EncryptedTotpSecret!, identifierOfTheUserAccountInTheseTests));
        Assert.Equal(10, backupCodesGivenToTheUserAtActivation.Count);
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests, SecurityEventType.TwoFactorActivation, SecurityEventResult.Success));

        var backupCodesInTheRealDatabase = await databaseContextOfThisChainTest.BackupCodes.AsNoTracking().Where(backupCodeSearchedInTheDatabaseTable => backupCodeSearchedInTheDatabaseTable.UserId == identifierOfTheUserAccountInTheseTests).ToListAsync();
        Assert.Equal(10, backupCodesInTheRealDatabase.Count);
        foreach (var backupCodeInTheRealDatabase in backupCodesInTheRealDatabase)
        {
            Assert.StartsWith("$argon2id$", backupCodeInTheRealDatabase.CodeHash);
            Assert.Null(backupCodeInTheRealDatabase.UsedAt);
        }

        var resultOfTheLoginWithThePasswordAlone = await LogInWithThePasswordLikeOneRealRequestAsync(databaseContextOfThisChainTest);
        Assert.True(resultOfTheLoginWithThePasswordAlone.IsWaitingForTwoFactorCode);
        Assert.Equal(0, await databaseContextOfThisChainTest.UserSessions.CountAsync(sessionSearchedInTheDatabaseTable => sessionSearchedInTheDatabaseTable.UserId == identifierOfTheUserAccountInTheseTests));
        Assert.Equal(1, (await ReadAccountBackFromTheRealDatabaseAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests)).FailedLoginAttemptCount);

        WaitForTheNextCodeOnThePhone();
        var resultOfTheCodeOfTheVisitor = await FinishLoginLikeOneRealRequestAsync(databaseContextOfThisChainTest, resultOfTheLoginWithThePasswordAlone.WaitingLoginTokenToPutInTheCookie!, CalculateCodeShownOnThePhoneNow(secretSharedWithThePhoneOfTheUser));

        Assert.True(resultOfTheCodeOfTheVisitor.IsLoggedIn);
        Assert.Equal(1, await databaseContextOfThisChainTest.UserSessions.CountAsync(sessionSearchedInTheDatabaseTable => sessionSearchedInTheDatabaseTable.UserId == identifierOfTheUserAccountInTheseTests));
        Assert.Equal(1, await databaseContextOfThisChainTest.Devices.CountAsync(deviceSearchedInTheDatabaseTable => deviceSearchedInTheDatabaseTable.UserId == identifierOfTheUserAccountInTheseTests));
        Assert.Equal(0, (await ReadAccountBackFromTheRealDatabaseAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests)).FailedLoginAttemptCount);
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests, SecurityEventType.Login, SecurityEventResult.Success));

        var serviceOfTheSessionsBeingTested = new UserSessionService(new UserSessionRepository(databaseContextOfThisChainTest), realGeneratorOfTheTokensInTheseTests, clockThatOnlyMovesWhenTestsAskIt);
        Assert.Equal(identifierOfTheUserAccountInTheseTests, await serviceOfTheSessionsBeingTested.FindOwnerOfTheActiveSessionThenRecordItsActivityAsync(resultOfTheCodeOfTheVisitor.SessionTokenToPutInTheCookie!));
    }

    [Fact]
    public async Task WrongCodesAreWrittenInTheRealJournalAndOpenNothing()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var identifierOfTheUserAccountInTheseTests = await RegisterThenConfirmNewUserAccountAsync(databaseContextOfThisChainTest);
        var (secretSharedWithThePhoneOfTheUser, _) = await ActivateTwoFactorLikeTwoRealRequestsAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests);
        var resultOfTheLoginWithThePasswordAlone = await LogInWithThePasswordLikeOneRealRequestAsync(databaseContextOfThisChainTest);
        var codeThatIsWrongInThisTest = TwoFactorTestTools.FindAuthenticatorCodeThatIsWrongAtThisDate(secretSharedWithThePhoneOfTheUser, clockThatOnlyMovesWhenTestsAskIt.GetUtcNow());

        for (var numberOfTheWrongCodeBeingTried = 1; numberOfTheWrongCodeBeingTried <= 4; numberOfTheWrongCodeBeingTried++)
        {
            var resultOfOneWrongCodeOfTheVisitor = await FinishLoginLikeOneRealRequestAsync(databaseContextOfThisChainTest, resultOfTheLoginWithThePasswordAlone.WaitingLoginTokenToPutInTheCookie!, codeThatIsWrongInThisTest);
            Assert.NotNull(resultOfOneWrongCodeOfTheVisitor.WrongCodeErrorMessage);
        }

        var resultOfTheFifthWrongCode = await FinishLoginLikeOneRealRequestAsync(databaseContextOfThisChainTest, resultOfTheLoginWithThePasswordAlone.WaitingLoginTokenToPutInTheCookie!, codeThatIsWrongInThisTest);

        Assert.NotNull(resultOfTheFifthWrongCode.LoginMustRestartErrorMessage);
        Assert.Equal(5, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests, SecurityEventType.Login, SecurityEventResult.Failure));
        Assert.Equal(0, await databaseContextOfThisChainTest.UserSessions.CountAsync(sessionSearchedInTheDatabaseTable => sessionSearchedInTheDatabaseTable.UserId == identifierOfTheUserAccountInTheseTests));
    }

    // Les vrais codes passent par le vrai Argon2 : un code de secours n'est utilisable qu'une fois, écrit avec ou sans son tiret.
    [Fact]
    public async Task BackupCodeWorksOnceWithTheRealHasherOnTheRealDatabase()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var identifierOfTheUserAccountInTheseTests = await RegisterThenConfirmNewUserAccountAsync(databaseContextOfThisChainTest);
        var (_, backupCodesGivenToTheUserAtActivation) = await ActivateTwoFactorLikeTwoRealRequestsAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests);

        var firstLoginOfTheAccountWithThePassword = await LogInWithThePasswordLikeOneRealRequestAsync(databaseContextOfThisChainTest);
        var resultOfTheFirstUseOfTheCode = await FinishLoginLikeOneRealRequestAsync(databaseContextOfThisChainTest, firstLoginOfTheAccountWithThePassword.WaitingLoginTokenToPutInTheCookie!, backupCodesGivenToTheUserAtActivation[7]);
        Assert.True(resultOfTheFirstUseOfTheCode.IsLoggedIn);
        Assert.Equal(1, await databaseContextOfThisChainTest.BackupCodes.CountAsync(backupCodeSearchedInTheDatabaseTable => backupCodeSearchedInTheDatabaseTable.UserId == identifierOfTheUserAccountInTheseTests && backupCodeSearchedInTheDatabaseTable.UsedAt == clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime));

        var secondLoginOfTheAccountWithThePassword = await LogInWithThePasswordLikeOneRealRequestAsync(databaseContextOfThisChainTest);
        var resultOfTheSecondUseOfTheCode = await FinishLoginLikeOneRealRequestAsync(databaseContextOfThisChainTest, secondLoginOfTheAccountWithThePassword.WaitingLoginTokenToPutInTheCookie!, backupCodesGivenToTheUserAtActivation[7].Replace("-", "").ToLowerInvariant());
        Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, resultOfTheSecondUseOfTheCode.WrongCodeErrorMessage);

        var resultOfTheLoginWithAnotherBackupCode = await FinishLoginLikeOneRealRequestAsync(databaseContextOfThisChainTest, secondLoginOfTheAccountWithThePassword.WaitingLoginTokenToPutInTheCookie!, backupCodesGivenToTheUserAtActivation[2].Replace("-", "").ToLowerInvariant());
        Assert.True(resultOfTheLoginWithAnotherBackupCode.IsLoggedIn);
    }

    // Le verrou réel : 5 connexions avec le bon mot de passe, puis le propriétaire entre avec son code et le blocage disparaît.
    [Fact]
    public async Task LoginsWithTheRightPasswordOnlyReallyLockTheAccountAndTheRightCodeRemovesTheLock()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var identifierOfTheUserAccountInTheseTests = await RegisterThenConfirmNewUserAccountAsync(databaseContextOfThisChainTest);
        var (secretSharedWithThePhoneOfTheUser, _) = await ActivateTwoFactorLikeTwoRealRequestsAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests);
        WaitForTheNextCodeOnThePhone();

        AccountLoginResult resultOfTheLastLogin = new AccountLoginResult();
        for (var numberOfTheLoginBeingTried = 1; numberOfTheLoginBeingTried <= 5; numberOfTheLoginBeingTried++)
        {
            resultOfTheLastLogin = await LogInWithThePasswordLikeOneRealRequestAsync(databaseContextOfThisChainTest);
            Assert.True(resultOfTheLastLogin.IsWaitingForTwoFactorCode);
        }

        var accountReadAgainAfterTheLock = await ReadAccountBackFromTheRealDatabaseAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests);
        Assert.NotNull(accountReadAgainAfterTheLock.LockoutEndsAt);
        Assert.Equal(1, accountReadAgainAfterTheLock.LockoutCount);
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests, SecurityEventType.Lockout, SecurityEventResult.Success));
        Assert.Single(queueThatKeepsTheEmailRequestsInMemory.RequestsAddedToTheBackgroundQueue);

        var resultOfTheLoginDuringTheLock = await LogInWithThePasswordLikeOneRealRequestAsync(databaseContextOfThisChainTest);
        Assert.False(resultOfTheLoginDuringTheLock.IsWaitingForTwoFactorCode);

        var resultOfTheCodeOfTheVisitor = await FinishLoginLikeOneRealRequestAsync(databaseContextOfThisChainTest, resultOfTheLastLogin.WaitingLoginTokenToPutInTheCookie!, CalculateCodeShownOnThePhoneNow(secretSharedWithThePhoneOfTheUser));

        Assert.True(resultOfTheCodeOfTheVisitor.IsLoggedIn);
        var accountReadAgainAfterTheLogin = await ReadAccountBackFromTheRealDatabaseAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests);
        Assert.Null(accountReadAgainAfterTheLogin.LockoutEndsAt);
        Assert.Equal(0, accountReadAgainAfterTheLogin.FailedLoginAttemptCount);
        Assert.Equal(0, accountReadAgainAfterTheLogin.LockoutCount);
    }

    // La fin du cycle : désactiver efface vraiment tout, l'événement passe la contrainte de la vraie base, et réactiver donne 10 nouveaux codes.
    [Fact]
    public async Task DeactivationThenReactivationWorkOnTheRealDatabase()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var identifierOfTheUserAccountInTheseTests = await RegisterThenConfirmNewUserAccountAsync(databaseContextOfThisChainTest);
        var (secretOfTheFirstPhoneOfTheUser, firstBackupCodesOfTheUser) = await ActivateTwoFactorLikeTwoRealRequestsAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests);
        var hashOfTheFirstBackupCode = (await databaseContextOfThisChainTest.BackupCodes.AsNoTracking().FirstAsync(backupCodeSearchedInTheDatabaseTable => backupCodeSearchedInTheDatabaseTable.UserId == identifierOfTheUserAccountInTheseTests)).CodeHash;

        var resultOfTheDeactivationOfTheTwoFactor = await DeactivateLikeOneRealRequestAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests, firstBackupCodesOfTheUser[0]);

        Assert.True(resultOfTheDeactivationOfTheTwoFactor.IsTwoFactorDeactivated);
        var accountReadAgainAfterTheDeactivation = await ReadAccountBackFromTheRealDatabaseAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests);
        Assert.False(accountReadAgainAfterTheDeactivation.IsTwoFactorEnabled);
        Assert.Null(accountReadAgainAfterTheDeactivation.EncryptedTotpSecret);
        Assert.Equal(0, await databaseContextOfThisChainTest.BackupCodes.CountAsync(backupCodeSearchedInTheDatabaseTable => backupCodeSearchedInTheDatabaseTable.UserId == identifierOfTheUserAccountInTheseTests));
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests, SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Success));

        var resultOfTheLoginWithoutAnyCode = await LogInWithThePasswordLikeOneRealRequestAsync(databaseContextOfThisChainTest);
        Assert.True(resultOfTheLoginWithoutAnyCode.IsLoggedIn);

        WaitForTheNextCodeOnThePhone();
        var (secretOfTheSecondPhoneOfTheUser, _) = await ActivateTwoFactorLikeTwoRealRequestsAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests);

        Assert.NotEqual(secretOfTheFirstPhoneOfTheUser, secretOfTheSecondPhoneOfTheUser);
        Assert.Equal(10, await databaseContextOfThisChainTest.BackupCodes.CountAsync(backupCodeSearchedInTheDatabaseTable => backupCodeSearchedInTheDatabaseTable.UserId == identifierOfTheUserAccountInTheseTests));
        Assert.False(await databaseContextOfThisChainTest.BackupCodes.AnyAsync(backupCodeSearchedInTheDatabaseTable => backupCodeSearchedInTheDatabaseTable.CodeHash == hashOfTheFirstBackupCode));
    }

    // Un mot de passe faux sur une désactivation laisse tout en place et s'écrit au vrai journal.
    [Fact]
    public async Task RefusedActivationAndDeactivationAreWrittenInTheRealJournal()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var identifierOfTheUserAccountInTheseTests = await RegisterThenConfirmNewUserAccountAsync(databaseContextOfThisChainTest);

        await CreateRealTwoFactorActivationServiceOnThisContext(databaseContextOfThisChainTest).StartActivationWithThePasswordOfTheUserAsync(identifierOfTheUserAccountInTheseTests, "Mauvais-Mot-De-Passe-2", IpAddressOfTheVisitorInTheseTests);
        databaseContextOfThisChainTest.ChangeTracker.Clear();
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests, SecurityEventType.TwoFactorActivation, SecurityEventResult.Failure));

        await ActivateTwoFactorLikeTwoRealRequestsAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests);

        var resultOfTheDeactivationOfTheTwoFactor = await DeactivateLikeOneRealRequestAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests, "000000");

        Assert.False(resultOfTheDeactivationOfTheTwoFactor.IsTwoFactorDeactivated);
        Assert.True((await ReadAccountBackFromTheRealDatabaseAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests)).IsTwoFactorEnabled);
        Assert.Equal(1, await CountSecurityEventsOfThisAccountAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests, SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Failure));
    }

    // GR Auth : la récupération garde la double authentification, annule les connexions en attente, et la suivante demande le code.
    [Fact]
    public async Task PasswordRecoveryKeepsTheTwoFactorOnTheRealDatabase()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var identifierOfTheUserAccountInTheseTests = await RegisterThenConfirmNewUserAccountAsync(databaseContextOfThisChainTest);
        var (secretSharedWithThePhoneOfTheUser, _) = await ActivateTwoFactorLikeTwoRealRequestsAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests);
        WaitForTheNextCodeOnThePhone();
        var loginOfTheThiefWithTheStolenPassword = await LogInWithThePasswordLikeOneRealRequestAsync(databaseContextOfThisChainTest);

        var generatedTokenOfTheRecoveryLink = realGeneratorOfTheTokensInTheseTests.GenerateNewRandomSecureTokenWithItsHash();
        await new UserAccountRepository(databaseContextOfThisChainTest).ReplaceEmailTokenOfThisTypeForThisAccountAsync(new EmailToken
        {
            Type = EmailTokenType.PasswordRecovery,
            TokenHash = generatedTokenOfTheRecoveryLink.HashToStoreInTheDatabase,
            ExpiresAt = clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddMinutes(30),
            UserId = identifierOfTheUserAccountInTheseTests
        });
        databaseContextOfThisChainTest.ChangeTracker.Clear();
        var serviceThatChangesTheForgottenPasswords = new PasswordResetService(
            new UserAccountRepository(databaseContextOfThisChainTest),
            new UserSessionRepository(databaseContextOfThisChainTest),
            realHasherOfThePasswordsInTheseTests,
            realGeneratorOfTheTokensInTheseTests,
            clockThatOnlyMovesWhenTestsAskIt);
        var resultOfTheRecoveryOfThePassword = await serviceThatChangesTheForgottenPasswords.ResetPasswordWithLinkTokenAsync(generatedTokenOfTheRecoveryLink.TokenGivenOnlyToTheVisitor, "Nouveau-Mot-De-Passe-3", IpAddressOfTheVisitorInTheseTests);
        databaseContextOfThisChainTest.ChangeTracker.Clear();
        Assert.True(resultOfTheRecoveryOfThePassword.IsPasswordChanged);

        var resultOfTheCodeOfTheThief = await FinishLoginLikeOneRealRequestAsync(databaseContextOfThisChainTest, loginOfTheThiefWithTheStolenPassword.WaitingLoginTokenToPutInTheCookie!, CalculateCodeShownOnThePhoneNow(secretSharedWithThePhoneOfTheUser));
        Assert.Equal(TwoFactorLoginService.ErrorMessageForLoginThatMustRestart, resultOfTheCodeOfTheThief.LoginMustRestartErrorMessage);

        var accountReadAgainAfterTheRecovery = await ReadAccountBackFromTheRealDatabaseAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests);
        Assert.True(accountReadAgainAfterTheRecovery.IsTwoFactorEnabled);
        Assert.NotNull(accountReadAgainAfterTheRecovery.EncryptedTotpSecret);

        var loginOfTheOwnerWithTheNewPassword = await LogInWithThePasswordLikeOneRealRequestAsync(databaseContextOfThisChainTest, "Nouveau-Mot-De-Passe-3");
        Assert.True(loginOfTheOwnerWithTheNewPassword.IsWaitingForTwoFactorCode);
        var resultOfTheCodeOfTheOwner = await FinishLoginLikeOneRealRequestAsync(databaseContextOfThisChainTest, loginOfTheOwnerWithTheNewPassword.WaitingLoginTokenToPutInTheCookie!, CalculateCodeShownOnThePhoneNow(secretSharedWithThePhoneOfTheUser));
        Assert.True(resultOfTheCodeOfTheOwner.IsLoggedIn);
    }

    // Le service de nettoyage ou d'effacement du compte passe par ON DELETE CASCADE : les codes de secours partent avec le compte.
    [Fact]
    public async Task BackupCodesDisappearWithTheAccountOnTheRealDatabase()
    {
        await using var databaseContextOfThisChainTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisChainTest.Database.BeginTransactionAsync();
        var identifierOfTheUserAccountInTheseTests = await RegisterThenConfirmNewUserAccountAsync(databaseContextOfThisChainTest);
        await ActivateTwoFactorLikeTwoRealRequestsAsync(databaseContextOfThisChainTest, identifierOfTheUserAccountInTheseTests);

        await databaseContextOfThisChainTest.Users.Where(userSearchedInTheDatabaseTable => userSearchedInTheDatabaseTable.Id == identifierOfTheUserAccountInTheseTests).ExecuteDeleteAsync();

        Assert.Equal(0, await databaseContextOfThisChainTest.BackupCodes.CountAsync(backupCodeSearchedInTheDatabaseTable => backupCodeSearchedInTheDatabaseTable.UserId == identifierOfTheUserAccountInTheseTests));
    }
}
