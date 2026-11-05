using System.Security.Cryptography;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Application.TwoFactorServicesWithFakeTools;

namespace WalletFlow.Tests.Application;

// Vérifie la connexion d'un compte avec double authentification (GR Auth, SS §2, §3, §6 et §11).
public class TwoFactorLoginServiceTests
{
    private readonly TwoFactorServicesWithFakeTools servicesWithFakeToolsOfTheseTests = new TwoFactorServicesWithFakeTools();

    // --- Le mot de passe ne suffit pas

    [Fact]
    public async Task RightPasswordAloneOpensNoSessionAndKeepsTheAttemptCounted()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();

        var resultOfTheLoginWithThePassword = await servicesWithFakeToolsOfTheseTests.TryToLogInWithThisPasswordAsync(PasswordOfTheAccountInTheseTests);

        Assert.True(resultOfTheLoginWithThePassword.IsWaitingForTwoFactorCode);
        Assert.False(resultOfTheLoginWithThePassword.IsLoggedIn);
        Assert.Null(resultOfTheLoginWithThePassword.SessionTokenToPutInTheCookie);
        Assert.Null(resultOfTheLoginWithThePassword.DeviceTokenToPutInTheCookie);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedDevices);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
        Assert.Equal(1, activatedAccountUsedByThisTest.UserAccount.FailedLoginAttemptCount);
    }

    [Fact]
    public async Task WrongPasswordOnAnActivatedAccountNeverAsksForTheCode()
    {
        servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();

        var resultOfTheLoginWithThePassword = await servicesWithFakeToolsOfTheseTests.TryToLogInWithThisPasswordAsync(WrongPasswordTypedInTheseTests);

        Assert.False(resultOfTheLoginWithThePassword.IsWaitingForTwoFactorCode);
        Assert.Equal(AccountLoginService.ErrorMessageForWrongLoginCredentials, resultOfTheLoginWithThePassword.WrongCredentialsErrorMessage);
    }

    [Fact]
    public async Task AccountWithoutTwoFactorStillLogsInWithItsPasswordAlone()
    {
        servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();

        var resultOfTheLoginWithThePassword = await servicesWithFakeToolsOfTheseTests.TryToLogInWithThisPasswordAsync(PasswordOfTheAccountInTheseTests);

        Assert.True(resultOfTheLoginWithThePassword.IsLoggedIn);
        Assert.False(resultOfTheLoginWithThePassword.IsWaitingForTwoFactorCode);
    }

    // Sans le contrôle du secret, une connexion lancée juste avant l'activation s'ouvrirait sans code.
    [Fact]
    public async Task TwoFactorActivatedDuringThePasswordCheckRefusesTheLoginWithoutCode()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        servicesWithFakeToolsOfTheseTests.HasherThatCountsTheComparedPasswords.ActionRunWhenOnePasswordIsCompared = () =>
        {
            accountUsedByThisTwoFactorTest.IsTwoFactorEnabled = true;
            accountUsedByThisTwoFactorTest.EncryptedTotpSecret = "secret-chiffre-par-une-autre-requete";
        };

        var resultOfTheLoginWithThePassword = await servicesWithFakeToolsOfTheseTests.TryToLogInWithThisPasswordAsync(PasswordOfTheAccountInTheseTests);

        Assert.False(resultOfTheLoginWithThePassword.IsLoggedIn);
        Assert.Equal(AccountLoginService.ErrorMessageForWrongLoginCredentials, resultOfTheLoginWithThePassword.WrongCredentialsErrorMessage);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // --- Le bon code

    // Seul le bon code rend la connexion réussie : les compteurs repartent alors à zéro (GR Auth).
    [Fact]
    public async Task RightCodeOpensTheSessionAndResetsEveryLoginCounter()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        activatedAccountUsedByThisTest.UserAccount.LockoutCount = 3;
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(
            waitingLoginTokenOfTheVisitor,
            servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        Assert.True(resultOfTheCodeAttemptOfTheVisitor.IsLoggedIn);
        Assert.NotNull(resultOfTheCodeAttemptOfTheVisitor.DeviceTokenToPutInTheCookie);
        var sessionSavedInTheFakeDatabase = Assert.Single(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
        Assert.Equal(activatedAccountUsedByThisTest.UserAccount.Id, sessionSavedInTheFakeDatabase.UserId);
        Assert.Equal(servicesWithFakeToolsOfTheseTests.RealGeneratorOfTheTokens.ConvertSecureTokenIntoItsHash(resultOfTheCodeAttemptOfTheVisitor.SessionTokenToPutInTheCookie!), sessionSavedInTheFakeDatabase.Id);
        Assert.Single(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedDevices);
        Assert.Equal(0, activatedAccountUsedByThisTest.UserAccount.FailedLoginAttemptCount);
        Assert.Equal(0, activatedAccountUsedByThisTest.UserAccount.LockoutCount);
        Assert.Null(activatedAccountUsedByThisTest.UserAccount.LockoutEndsAt);

        var securityEventOfTheLoginWithTheCode = Assert.Single(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.Login, securityEventOfTheLoginWithTheCode.Type);
        Assert.Equal(SecurityEventResult.Success, securityEventOfTheLoginWithTheCode.Result);
        Assert.Equal(IpAddressOfTheVisitorInTheseTests, securityEventOfTheLoginWithTheCode.IpAddress);
        Assert.Equal(activatedAccountUsedByThisTest.UserAccount.Id, securityEventOfTheLoginWithTheCode.UserId);
    }

    [Fact]
    public async Task WaitingLoginCannotServeASecondTimeAfterASuccessfulLogin()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));
        servicesWithFakeToolsOfTheseTests.WaitForTheNextCodeOnThePhone();

        var resultOfTheSecondCodeAttempt = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        Assert.Equal(TwoFactorLoginService.ErrorMessageForLoginThatMustRestart, resultOfTheSecondCodeAttempt.LoginMustRestartErrorMessage);
        Assert.Single(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // Le téléphone et le serveur n'ont jamais tout à fait la même heure (SS §2).
    [Theory]
    [InlineData(-30)]
    [InlineData(0)]
    [InlineData(30)]
    public async Task CodeOfTheCurrentPreviousOrNextPeriodIsAccepted(int secondsBetweenThePhoneAndTheServer)
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        var codeShownOnThePhoneInThisTest = TwoFactorTestTools.CalculateAuthenticatorCodeShownAtThisDate(
            activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication,
            servicesWithFakeToolsOfTheseTests.ClockThatOnlyMovesWhenTestsAskIt.GetUtcNow().AddSeconds(secondsBetweenThePhoneAndTheServer));

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, codeShownOnThePhoneInThisTest);

        Assert.True(resultOfTheCodeAttemptOfTheVisitor.IsLoggedIn);
    }

    [Theory]
    [InlineData(-60)]
    [InlineData(60)]
    public async Task CodeOfAPeriodTooFarAwayIsRefused(int secondsBetweenThePhoneAndTheServer)
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        var codeShownOnThePhoneInThisTest = TwoFactorTestTools.CalculateAuthenticatorCodeShownAtThisDate(
            activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication,
            servicesWithFakeToolsOfTheseTests.ClockThatOnlyMovesWhenTestsAskIt.GetUtcNow().AddSeconds(secondsBetweenThePhoneAndTheServer));

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, codeShownOnThePhoneInThisTest);

        Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, resultOfTheCodeAttemptOfTheVisitor.WrongCodeErrorMessage);
    }

    // Les applications affichent souvent le code avec un espace au milieu.
    [Fact]
    public async Task CodeTypedWithASpaceInTheMiddleIsAccepted()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        var codeShownOnThePhoneInThisTest = servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication);

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, codeShownOnThePhoneInThisTest.Substring(0, 3) + " " + codeShownOnThePhoneInThisTest.Substring(3));

        Assert.True(resultOfTheCodeAttemptOfTheVisitor.IsLoggedIn);
    }

    // SS §2 : quelqu'un qui a vu passer un code ne peut pas s'en resservir, même pendant qu'il reste valable.
    [Fact]
    public async Task CodeAlreadyUsedToLogInIsRefusedUntilThePhoneShowsANewOne()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var codeShownOnThePhoneInThisTest = servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication);
        var tokenOfTheFirstLoginWaitingItsCode = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(tokenOfTheFirstLoginWaitingItsCode, codeShownOnThePhoneInThisTest);

        var tokenOfTheSecondLoginWaitingItsCode = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        var resultOfTheLoginWithTheSameCode = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(tokenOfTheSecondLoginWaitingItsCode, codeShownOnThePhoneInThisTest);

        Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, resultOfTheLoginWithTheSameCode.WrongCodeErrorMessage);
        Assert.Single(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);

        servicesWithFakeToolsOfTheseTests.WaitForTheNextCodeOnThePhone();
        var resultOfTheLoginWithTheNextCode = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(tokenOfTheSecondLoginWaitingItsCode, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        Assert.True(resultOfTheLoginWithTheNextCode.IsLoggedIn);
    }

    // Un code plus ancien que le dernier utilisé ne vaut plus rien, même s'il n'a jamais servi.
    [Fact]
    public async Task CodeOlderThanTheLastOneUsedIsRefused()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var currentDateOfTheFakeClock = servicesWithFakeToolsOfTheseTests.ClockThatOnlyMovesWhenTestsAskIt.GetUtcNow();
        var tokenOfTheFirstLoginWaitingItsCode = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(tokenOfTheFirstLoginWaitingItsCode, TwoFactorTestTools.CalculateAuthenticatorCodeShownAtThisDate(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication, currentDateOfTheFakeClock.AddSeconds(30)));

        var tokenOfTheSecondLoginWaitingItsCode = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        var resultOfTheLoginWithTheOlderCode = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(tokenOfTheSecondLoginWaitingItsCode, TwoFactorTestTools.CalculateAuthenticatorCodeShownAtThisDate(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication, currentDateOfTheFakeClock));

        Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, resultOfTheLoginWithTheOlderCode.WrongCodeErrorMessage);
    }

    // --- Les mauvais codes

    [Fact]
    public async Task WrongCodeIsRefusedAndWrittenInTheJournalThenTheRightCodeStillWorks()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();

        var resultOfTheWrongCodeOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.FindCodeThatIsWrongNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        Assert.False(resultOfTheWrongCodeOfTheVisitor.IsLoggedIn);
        Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, resultOfTheWrongCodeOfTheVisitor.WrongCodeErrorMessage);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);

        var securityEventOfTheWrongCode = Assert.Single(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.Login, securityEventOfTheWrongCode.Type);
        Assert.Equal(SecurityEventResult.Failure, securityEventOfTheWrongCode.Result);
        Assert.Equal(IpAddressOfTheVisitorInTheseTests, securityEventOfTheWrongCode.IpAddress);
        Assert.Equal(activatedAccountUsedByThisTest.UserAccount.Id, securityEventOfTheWrongCode.UserId);

        var resultOfTheRightCodeOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));
        Assert.True(resultOfTheRightCodeOfTheVisitor.IsLoggedIn);
    }

    // SS §6 : 5 essais, puis il faut recommencer la connexion. Même le bon code ne passe plus.
    [Fact]
    public async Task FifthWrongCodeForcesTheVisitorToRestartEvenWithTheRightCodeAfterwards()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        var codeThatIsWrongInThisTest = servicesWithFakeToolsOfTheseTests.FindCodeThatIsWrongNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication);

        for (var numberOfTheWrongCodeBeingTried = 1; numberOfTheWrongCodeBeingTried <= 4; numberOfTheWrongCodeBeingTried++)
        {
            var resultOfOneWrongCodeOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, codeThatIsWrongInThisTest);
            Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, resultOfOneWrongCodeOfTheVisitor.WrongCodeErrorMessage);
        }

        var resultOfTheFifthWrongCode = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, codeThatIsWrongInThisTest);
        Assert.Equal(TwoFactorLoginService.ErrorMessageForLoginThatMustRestart, resultOfTheFifthWrongCode.LoginMustRestartErrorMessage);
        Assert.Null(resultOfTheFifthWrongCode.WrongCodeErrorMessage);

        var resultOfTheRightCodeTooLate = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));
        Assert.Equal(TwoFactorLoginService.ErrorMessageForLoginThatMustRestart, resultOfTheRightCodeTooLate.LoginMustRestartErrorMessage);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
        Assert.Equal(5, servicesWithFakeToolsOfTheseTests.CountSecurityEventsInTheJournal(SecurityEventType.Login, SecurityEventResult.Failure));
    }

    [Fact]
    public async Task RightCodeOnTheFifthTryStillOpensTheSession()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        var codeThatIsWrongInThisTest = servicesWithFakeToolsOfTheseTests.FindCodeThatIsWrongNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication);

        for (var numberOfTheWrongCodeBeingTried = 1; numberOfTheWrongCodeBeingTried <= 4; numberOfTheWrongCodeBeingTried++)
        {
            await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, codeThatIsWrongInThisTest);
        }

        var resultOfTheFifthCodeOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        Assert.True(resultOfTheFifthCodeOfTheVisitor.IsLoggedIn);
    }

    // Un texte sans la forme d'un code est refusé sans calcul : le secret illisible de ce compte ferait échouer tout déchiffrement,
    // et aucun Argon2 ne tourne.
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    [InlineData("١٢٣٤٥٦")]
    [InlineData("１２３４５６")]
    [InlineData("12345\0")]
    [InlineData("ABCDEFGHI0")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("' OR 1=1 --")]
    public async Task TextWithoutTheFormatOfACodeIsRefusedWithoutAnyCalculation(string textTypedByTheVisitorInThisTest)
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        activatedAccountUsedByThisTest.UserAccount.EncryptedTotpSecret = "secret-illisible";
        var numberOfComparisonsBeforeTheCode = servicesWithFakeToolsOfTheseTests.HasherThatCountsTheComparedPasswords.NumberOfPasswordsCompared;

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, textTypedByTheVisitorInThisTest);

        Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, resultOfTheCodeAttemptOfTheVisitor.WrongCodeErrorMessage);
        Assert.Equal(numberOfComparisonsBeforeTheCode, servicesWithFakeToolsOfTheseTests.HasherThatCountsTheComparedPasswords.NumberOfPasswordsCompared);
    }

    [Fact]
    public async Task EnormousTextIsRefusedLikeAnyWrongCode()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, new string('1', 100000));

        Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, resultOfTheCodeAttemptOfTheVisitor.WrongCodeErrorMessage);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // --- Une attente de 5 minutes au plus

    [Fact]
    public async Task WaitingLoginStillWorksJustBeforeTheEndOfTheFiveMinutes()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();

        servicesWithFakeToolsOfTheseTests.ClockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        Assert.True(resultOfTheCodeAttemptOfTheVisitor.IsLoggedIn);
    }

    [Fact]
    public async Task WaitingLoginEndsExactlyAfterFiveMinutes()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();

        servicesWithFakeToolsOfTheseTests.ClockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(5));
        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        Assert.Equal(TwoFactorLoginService.ErrorMessageForLoginThatMustRestart, resultOfTheCodeAttemptOfTheVisitor.LoginMustRestartErrorMessage);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // Rien n'est écrit au journal : personne ne doit pouvoir le remplir sans avoir donné le bon mot de passe.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("jeton-inconnu")]
    public async Task MissingOrUnknownTokenAsksToRestartWithoutWritingInTheJournal(string? waitingLoginTokenSentInThisTest)
    {
        servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenSentInThisTest, "123456");

        Assert.Equal(TwoFactorLoginService.ErrorMessageForLoginThatMustRestart, resultOfTheCodeAttemptOfTheVisitor.LoginMustRestartErrorMessage);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
    }

    // --- Les codes de secours

    [Fact]
    public async Task BackupCodeOpensTheSessionAndIsMarkedAsUsed()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        var backupCodeUsedInThisTest = activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[3];

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, TwoFactorAuthenticationRules.ConvertBackupCodeIntoItsDisplayedForm(backupCodeUsedInThisTest));

        Assert.True(resultOfTheCodeAttemptOfTheVisitor.IsLoggedIn);
        var backupCodesLeftUnusedAfterTheLogin = await servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.FindUnusedBackupCodesOfThisAccountAsync(activatedAccountUsedByThisTest.UserAccount.Id);
        Assert.Equal(9, backupCodesLeftUnusedAfterTheLogin.Count);
        var backupCodeJustUsedForTheLogin = servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes[3];
        Assert.Equal(servicesWithFakeToolsOfTheseTests.ClockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime, backupCodeJustUsedForTheLogin.UsedAt);
    }

    [Fact]
    public async Task BackupCodeAlreadyUsedIsRefusedAtTheNextLogin()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var backupCodeUsedInThisTest = activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[0];
        await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync(), backupCodeUsedInThisTest);

        var resultOfTheSecondUseOfTheCode = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync(), backupCodeUsedInThisTest);

        Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, resultOfTheSecondUseOfTheCode.WrongCodeErrorMessage);
        Assert.Single(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task BackupCodeIsAcceptedInLowercaseAndWithoutItsDash(bool backupCodeIsWrittenWithItsDash, bool backupCodeIsWrittenInLowercaseLetters)
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        var codeTypedByTheVisitorInThisTest = activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[0];

        if (backupCodeIsWrittenWithItsDash)
        {
            codeTypedByTheVisitorInThisTest = TwoFactorAuthenticationRules.ConvertBackupCodeIntoItsDisplayedForm(codeTypedByTheVisitorInThisTest);
        }

        if (backupCodeIsWrittenInLowercaseLetters)
        {
            codeTypedByTheVisitorInThisTest = codeTypedByTheVisitorInThisTest.ToLowerInvariant();
        }

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, codeTypedByTheVisitorInThisTest);

        Assert.True(resultOfTheCodeAttemptOfTheVisitor.IsLoggedIn);
    }

    [Fact]
    public async Task BackupCodeOfAnotherAccountIsRefused()
    {
        servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var otherAccountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase("autre-compte@exemple.test");
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, otherAccountUsedByThisTwoFactorTest.BackupCodesInTheirCleanForm[0]);

        Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, resultOfTheCodeAttemptOfTheVisitor.WrongCodeErrorMessage);
        var backupCodesLeftToTheOtherAccount = await servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.FindUnusedBackupCodesOfThisAccountAsync(otherAccountUsedByThisTwoFactorTest.UserAccount.Id);
        Assert.Equal(10, backupCodesLeftToTheOtherAccount.Count);
    }

    // Deux requêtes avec le même code de secours : celle qui arrive après le contrôle de l'autre ne doit rien ouvrir.
    [Fact]
    public async Task BackupCodeUsedByAnotherRequestDuringTheCheckOpensNothing()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        servicesWithFakeToolsOfTheseTests.HasherThatCountsTheComparedPasswords.ActionRunWhenOnePasswordIsCompared = () =>
            servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes[0].UsedAt = servicesWithFakeToolsOfTheseTests.ClockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime;

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[0]);

        Assert.Equal(TwoFactorLoginService.ErrorMessageForLoginThatMustRestart, resultOfTheCodeAttemptOfTheVisitor.LoginMustRestartErrorMessage);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    [Fact]
    public async Task BackupCodeEffacedDuringTheCheckOpensNothing()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        var backupCodeEffacedDuringTheCheck = servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes[0];
        servicesWithFakeToolsOfTheseTests.HasherThatCountsTheComparedPasswords.ActionRunWhenOnePasswordIsCompared = () =>
            servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes.Remove(backupCodeEffacedDuringTheCheck);

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[0]);

        Assert.Equal(TwoFactorLoginService.ErrorMessageForLoginThatMustRestart, resultOfTheCodeAttemptOfTheVisitor.LoginMustRestartErrorMessage);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // --- Le compte change pendant l'attente

    [Fact]
    public async Task PasswordChangedDuringTheWaitForcesTheVisitorToRestart()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        activatedAccountUsedByThisTest.UserAccount.PasswordHash = FakePasswordHasher.PrefixOfTheFakePasswordHashes + "Nouveau-Mot-De-Passe-3";

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        Assert.Equal(TwoFactorLoginService.ErrorMessageForLoginThatMustRestart, resultOfTheCodeAttemptOfTheVisitor.LoginMustRestartErrorMessage);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
        Assert.Equal(1, servicesWithFakeToolsOfTheseTests.CountSecurityEventsInTheJournal(SecurityEventType.Login, SecurityEventResult.Failure));
    }

    [Fact]
    public async Task PasswordChangedWhileTheCodeIsCheckedOpensNothing()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        servicesWithFakeToolsOfTheseTests.HasherThatCountsTheComparedPasswords.ActionRunWhenOnePasswordIsCompared = () =>
            activatedAccountUsedByThisTest.UserAccount.PasswordHash = FakePasswordHasher.PrefixOfTheFakePasswordHashes + "Nouveau-Mot-De-Passe-3";

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[0]);

        Assert.Equal(TwoFactorLoginService.ErrorMessageForLoginThatMustRestart, resultOfTheCodeAttemptOfTheVisitor.LoginMustRestartErrorMessage);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    [Fact]
    public async Task TwoFactorDeactivatedDuringTheWaitForcesTheVisitorToRestart()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        var codeShownOnThePhoneInThisTest = servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication);
        activatedAccountUsedByThisTest.UserAccount.IsTwoFactorEnabled = false;
        activatedAccountUsedByThisTest.UserAccount.EncryptedTotpSecret = null;

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, codeShownOnThePhoneInThisTest);

        Assert.Equal(TwoFactorLoginService.ErrorMessageForLoginThatMustRestart, resultOfTheCodeAttemptOfTheVisitor.LoginMustRestartErrorMessage);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // Une connexion abandonnée ne revient pas, même si le compte redevient comme avant : il faut redonner le mot de passe.
    [Fact]
    public async Task AbandonedWaitingLoginDoesNotComeBackWhenTheTwoFactorIsActivatedAgain()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        var encryptedSecretReadBeforeTheDeactivationOfTheAccount = activatedAccountUsedByThisTest.UserAccount.EncryptedTotpSecret;
        activatedAccountUsedByThisTest.UserAccount.IsTwoFactorEnabled = false;
        activatedAccountUsedByThisTest.UserAccount.EncryptedTotpSecret = null;
        await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, "123456");

        activatedAccountUsedByThisTest.UserAccount.IsTwoFactorEnabled = true;
        activatedAccountUsedByThisTest.UserAccount.EncryptedTotpSecret = encryptedSecretReadBeforeTheDeactivationOfTheAccount;
        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        Assert.Equal(TwoFactorLoginService.ErrorMessageForLoginThatMustRestart, resultOfTheCodeAttemptOfTheVisitor.LoginMustRestartErrorMessage);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // Le secret qui vient de servir à vérifier le code ne vaut plus : le propriétaire vient peut-être de changer de téléphone.
    [Fact]
    public async Task SecretChangedWhileTheCodeIsCheckedOpensNothing()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        servicesWithFakeToolsOfTheseTests.HasherThatCountsTheComparedPasswords.ActionRunWhenOnePasswordIsCompared = () =>
            activatedAccountUsedByThisTest.UserAccount.EncryptedTotpSecret = "secret-du-nouveau-telephone";

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[0]);

        Assert.Equal(TwoFactorLoginService.ErrorMessageForLoginThatMustRestart, resultOfTheCodeAttemptOfTheVisitor.LoginMustRestartErrorMessage);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // Le compte a disparu : le journal ne peut plus citer son identifiant, la vraie base refuserait l'écriture.
    [Fact]
    public async Task AccountEffacedDuringTheWaitForcesTheVisitorToRestartWithoutItsIdentifierInTheJournal()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsAccountsInMemory.SavedUsers.Remove(activatedAccountUsedByThisTest.UserAccount);

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, "123456");

        Assert.Equal(TwoFactorLoginService.ErrorMessageForLoginThatMustRestart, resultOfTheCodeAttemptOfTheVisitor.LoginMustRestartErrorMessage);
        var securityEventOfTheRefusalOfTheRequest = Assert.Single(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
        Assert.Equal(SecurityEventResult.Failure, securityEventOfTheRefusalOfTheRequest.Result);
        Assert.Null(securityEventOfTheRefusalOfTheRequest.UserId);
    }

    // GR Auth : une récupération ne désactive pas la double authentification, et le pirate d'une boîte mail ne peut pas s'en servir pour entrer.
    [Fact]
    public async Task PasswordRecoveryKeepsTheTwoFactorAndTheNextLoginStillAsksForTheCode()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var generatedTokenOfTheRecoveryLink = servicesWithFakeToolsOfTheseTests.RealGeneratorOfTheTokens.GenerateNewRandomSecureTokenWithItsHash();
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsAccountsInMemory.SavedEmailTokens.Add(new EmailToken
        {
            Type = EmailTokenType.PasswordRecovery,
            TokenHash = generatedTokenOfTheRecoveryLink.HashToStoreInTheDatabase,
            ExpiresAt = servicesWithFakeToolsOfTheseTests.ClockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddMinutes(30),
            UserId = activatedAccountUsedByThisTest.UserAccount.Id
        });
        var encryptedSecretReadBeforeThePasswordRecovery = activatedAccountUsedByThisTest.UserAccount.EncryptedTotpSecret;

        var resultOfTheRecoveryOfThePassword = await servicesWithFakeToolsOfTheseTests.CreatePasswordResetServiceWithFakeTools().ResetPasswordWithLinkTokenAsync(generatedTokenOfTheRecoveryLink.TokenGivenOnlyToTheVisitor, "Nouveau-Mot-De-Passe-3", IpAddressOfTheVisitorInTheseTests);

        Assert.True(resultOfTheRecoveryOfThePassword.IsPasswordChanged);
        Assert.True(activatedAccountUsedByThisTest.UserAccount.IsTwoFactorEnabled);
        Assert.Equal(encryptedSecretReadBeforeThePasswordRecovery, activatedAccountUsedByThisTest.UserAccount.EncryptedTotpSecret);
        Assert.Equal(10, servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes.Count);

        var resultOfTheNextLoginOfTheAccount = await servicesWithFakeToolsOfTheseTests.TryToLogInWithThisPasswordAsync("Nouveau-Mot-De-Passe-3");
        Assert.True(resultOfTheNextLoginOfTheAccount.IsWaitingForTwoFactorCode);
        Assert.False(resultOfTheNextLoginOfTheAccount.IsLoggedIn);
    }

    // --- Le blocage du compte

    // GR Auth : seul le bon code remet à zéro. Un voleur du mot de passe finit bloqué, et le propriétaire reçoit un seul e-mail.
    [Fact]
    public async Task FifthLoginWithTheRightPasswordLocksTheAccountAndWarnsTheOwnerOnce()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();

        for (var numberOfTheLoginBeingTried = 1; numberOfTheLoginBeingTried <= 4; numberOfTheLoginBeingTried++)
        {
            var resultOfOneLoginWithThePassword = await servicesWithFakeToolsOfTheseTests.TryToLogInWithThisPasswordAsync(PasswordOfTheAccountInTheseTests);
            Assert.True(resultOfOneLoginWithThePassword.IsWaitingForTwoFactorCode);
            Assert.Null(activatedAccountUsedByThisTest.UserAccount.LockoutEndsAt);
        }

        Assert.Empty(servicesWithFakeToolsOfTheseTests.QueueThatKeepsTheEmailRequestsInMemory.RequestsAddedToTheBackgroundQueue);

        var resultOfTheFifthLoginOfTheAccount = await servicesWithFakeToolsOfTheseTests.TryToLogInWithThisPasswordAsync(PasswordOfTheAccountInTheseTests);

        Assert.True(resultOfTheFifthLoginOfTheAccount.IsWaitingForTwoFactorCode);
        Assert.Equal(servicesWithFakeToolsOfTheseTests.ClockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddMinutes(15), activatedAccountUsedByThisTest.UserAccount.LockoutEndsAt);
        Assert.Equal(1, activatedAccountUsedByThisTest.UserAccount.LockoutCount);
        Assert.Equal(1, servicesWithFakeToolsOfTheseTests.CountSecurityEventsInTheJournal(SecurityEventType.Lockout, SecurityEventResult.Success));
        var emailRequestAddedForTheAccountLockout = Assert.Single(servicesWithFakeToolsOfTheseTests.QueueThatKeepsTheEmailRequestsInMemory.RequestsAddedToTheBackgroundQueue);
        Assert.Equal(activatedAccountUsedByThisTest.UserAccount.Id, emailRequestAddedForTheAccountLockout.IdentifierOfTheAccountJustLocked);
    }

    [Fact]
    public async Task LoginDuringTheLockIsRefusedEvenWithTheRightPassword()
    {
        servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        for (var numberOfTheLoginBeingTried = 1; numberOfTheLoginBeingTried <= 5; numberOfTheLoginBeingTried++)
        {
            await servicesWithFakeToolsOfTheseTests.TryToLogInWithThisPasswordAsync(PasswordOfTheAccountInTheseTests);
        }

        var resultOfTheSixthLoginOfTheAccount = await servicesWithFakeToolsOfTheseTests.TryToLogInWithThisPasswordAsync(PasswordOfTheAccountInTheseTests);

        Assert.False(resultOfTheSixthLoginOfTheAccount.IsWaitingForTwoFactorCode);
        Assert.Equal(AccountLoginService.ErrorMessageForWrongLoginCredentials, resultOfTheSixthLoginOfTheAccount.WrongCredentialsErrorMessage);
    }

    // Le vrai propriétaire a le mot de passe et le code : son 5e essai le fait entrer, et le blocage disparaît.
    [Fact]
    public async Task RightCodeAfterTheLockOpensTheSessionAndRemovesTheLock()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        string waitingLoginTokenOfTheFifthLogin = "";
        for (var numberOfTheLoginBeingTried = 1; numberOfTheLoginBeingTried <= 5; numberOfTheLoginBeingTried++)
        {
            waitingLoginTokenOfTheFifthLogin = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        }

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheFifthLogin, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        Assert.True(resultOfTheCodeAttemptOfTheVisitor.IsLoggedIn);
        Assert.Null(activatedAccountUsedByThisTest.UserAccount.LockoutEndsAt);
        Assert.Equal(0, activatedAccountUsedByThisTest.UserAccount.FailedLoginAttemptCount);
        Assert.Equal(0, activatedAccountUsedByThisTest.UserAccount.LockoutCount);
    }

    // Une série de mauvais codes ne bloque pas toute seule : c'est de recommencer la connexion qui compte (SS §6).
    [Fact]
    public async Task WrongCodesDoNotCountAsLoginAttemptsOfTheAccount()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        var codeThatIsWrongInThisTest = servicesWithFakeToolsOfTheseTests.FindCodeThatIsWrongNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication);

        for (var numberOfTheWrongCodeBeingTried = 1; numberOfTheWrongCodeBeingTried <= 5; numberOfTheWrongCodeBeingTried++)
        {
            await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, codeThatIsWrongInThisTest);
        }

        Assert.Equal(1, activatedAccountUsedByThisTest.UserAccount.FailedLoginAttemptCount);
    }

    // --- Les pannes

    [Fact]
    public async Task ConflictsWithOtherRequestsAreRetriedAndOpenOneSessionOnly()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.NumberOfConflictsBeforeTheLoginIsSaved = 9;

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        Assert.True(resultOfTheCodeAttemptOfTheVisitor.IsLoggedIn);
        Assert.Single(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    [Fact]
    public async Task TooManyConflictsAskToRestartAndOpenNothing()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.NumberOfConflictsBeforeTheLoginIsSaved = 10;

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        Assert.Equal(TwoFactorLoginService.ErrorMessageForLoginThatMustRestart, resultOfTheCodeAttemptOfTheVisitor.LoginMustRestartErrorMessage);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // Une panne ne fait croire à personne que la connexion a réussi. Le code est pris, mais le suivant fonctionne, sans recommencer.
    [Fact]
    public async Task DatabaseFailureDuringTheSaveIsNotHiddenAndTheNextCodeWorksAfterIt()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.DatabaseIsDownDuringThisTest = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication)));
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);

        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.DatabaseIsDownDuringThisTest = false;
        servicesWithFakeToolsOfTheseTests.WaitForTheNextCodeOnThePhone();
        var resultOfTheCodeAfterTheFailure = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        Assert.True(resultOfTheCodeAfterTheFailure.IsLoggedIn);
    }

    [Fact]
    public async Task JournalFailureOnAWrongCodeIsNotHidden()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSecurityEventsInMemory.DatabaseIsDownDuringThisTest = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.FindCodeThatIsWrongNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication)));
    }

    [Fact]
    public async Task DatabaseFailureWhileReadingTheBackupCodesIsNotHidden()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.DatabaseIsDownWhileReadingTheBackupCodes = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[0]));
    }

    // Une clé perdue ne se fait pas passer pour un mauvais code : l'erreur remonte, et elle sera vue dans le journal technique.
    [Fact]
    public async Task LostEncryptionKeyIsNotHiddenForAuthenticatorCodesAndLeavesTheBackupCodesWorking()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        servicesWithFakeToolsOfTheseTests.RealProtectorOfTheSecrets = new AesGcmTotpSecretProtector(TwoFactorTestTools.CreateNewEncryptionKeyForOneTest());

        await Assert.ThrowsAnyAsync<CryptographicException>(() => servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication)));
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);

        var resultOfTheLoginWithTheBackupCode = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[0]);
        Assert.True(resultOfTheLoginWithTheBackupCode.IsLoggedIn);
    }

    // --- Les cookies déjà présents

    [Fact]
    public async Task KnownDeviceKeepsItsTokenAndTheOldSessionOfTheBrowserIsClosed()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var generatedTokenOfTheKnownDevice = servicesWithFakeToolsOfTheseTests.RealGeneratorOfTheTokens.GenerateNewRandomSecureTokenWithItsHash();
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedDevices.Add(new Device { DeviceToken = generatedTokenOfTheKnownDevice.HashToStoreInTheDatabase, UserId = activatedAccountUsedByThisTest.UserAccount.Id });
        var generatedTokenOfTheOldSession = servicesWithFakeToolsOfTheseTests.RealGeneratorOfTheTokens.GenerateNewRandomSecureTokenWithItsHash();
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions.Add(new UserSession { Id = generatedTokenOfTheOldSession.HashToStoreInTheDatabase, UserId = activatedAccountUsedByThisTest.UserAccount.Id });
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();

        var resultOfTheCodeAttemptOfTheVisitor = await servicesWithFakeToolsOfTheseTests.CreateTwoFactorLoginServiceWithFakeTools().FinishLoginOfTheVisitorWithThisCodeAsync(new TwoFactorCodeAttemptFromVisitor
        {
            CodeTypedByTheVisitor = servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication),
            IpAddressOfTheVisitor = IpAddressOfTheVisitorInTheseTests,
            WaitingLoginTokenReadInTheCookie = waitingLoginTokenOfTheVisitor,
            DeviceTokenReadInTheCookie = generatedTokenOfTheKnownDevice.TokenGivenOnlyToTheVisitor,
            SessionTokenReadInTheCookie = generatedTokenOfTheOldSession.TokenGivenOnlyToTheVisitor
        });

        Assert.Equal(generatedTokenOfTheKnownDevice.TokenGivenOnlyToTheVisitor, resultOfTheCodeAttemptOfTheVisitor.DeviceTokenToPutInTheCookie);
        Assert.Single(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedDevices);
        var onlySessionLeftInTheFakeDatabase = Assert.Single(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSessionsInMemory.SavedSessions);
        Assert.NotEqual(generatedTokenOfTheOldSession.HashToStoreInTheDatabase, onlySessionLeftInTheFakeDatabase.Id);
    }
}
