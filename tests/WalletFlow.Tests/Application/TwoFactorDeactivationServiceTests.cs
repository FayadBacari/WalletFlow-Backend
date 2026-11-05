using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Application.TwoFactorServicesWithFakeTools;

namespace WalletFlow.Tests.Application;

// Vérifie la désactivation de la double authentification (GR Auth, SS §2 et §11).
public class TwoFactorDeactivationServiceTests
{
    private readonly TwoFactorServicesWithFakeTools servicesWithFakeToolsOfTheseTests = new TwoFactorServicesWithFakeTools();

    // Désactive avec ce mot de passe et ce code.
    private async Task<TwoFactorDeactivationResult> DeactivateWithThisPasswordAndCodeAsync(User accountBeingDeactivatedInThisTest, string passwordTypedByTheUserInThisTest, string codeTypedByTheUserInThisTest)
    {
        return await servicesWithFakeToolsOfTheseTests.CreateTwoFactorDeactivationServiceWithFakeTools().DeactivateTwoFactorWithPasswordAndCodeOfTheUserAsync(
            accountBeingDeactivatedInThisTest.Id,
            passwordTypedByTheUserInThisTest,
            codeTypedByTheUserInThisTest,
            IpAddressOfTheVisitorInTheseTests);
    }

    // Vérifie que le compte est resté activé, avec son secret et ses 10 codes.
    private void CheckThatTheAccountIsStillActivated(ActivatedAccountOfTheseTests activatedAccountUsedByThisTest)
    {
        Assert.True(activatedAccountUsedByThisTest.UserAccount.IsTwoFactorEnabled);
        Assert.NotNull(activatedAccountUsedByThisTest.UserAccount.EncryptedTotpSecret);
        Assert.Equal(10, servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes.Count);
    }

    [Fact]
    public async Task RightPasswordAndCodeEraseTheSecretAndEveryBackupCode()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes[0].UsedAt = servicesWithFakeToolsOfTheseTests.ClockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime;

        var resultOfTheDeactivationOfTheTwoFactor = await DeactivateWithThisPasswordAndCodeAsync(
            activatedAccountUsedByThisTest.UserAccount,
            PasswordOfTheAccountInTheseTests,
            servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        Assert.True(resultOfTheDeactivationOfTheTwoFactor.IsTwoFactorDeactivated);
        Assert.False(activatedAccountUsedByThisTest.UserAccount.IsTwoFactorEnabled);
        Assert.Null(activatedAccountUsedByThisTest.UserAccount.EncryptedTotpSecret);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes);

        var securityEventOfTheTwoFactorDeactivation = Assert.Single(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.TwoFactorDeactivation, securityEventOfTheTwoFactorDeactivation.Type);
        Assert.Equal(SecurityEventResult.Success, securityEventOfTheTwoFactorDeactivation.Result);
        Assert.Equal(IpAddressOfTheVisitorInTheseTests, securityEventOfTheTwoFactorDeactivation.IpAddress);
        Assert.Equal(activatedAccountUsedByThisTest.UserAccount.Id, securityEventOfTheTwoFactorDeactivation.UserId);
    }

    // Le seul moyen de changer de téléphone après en avoir perdu un.
    [Fact]
    public async Task BackupCodeAlsoDeactivatesTheTwoFactorAuthentication()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();

        var resultOfTheDeactivationOfTheTwoFactor = await DeactivateWithThisPasswordAndCodeAsync(activatedAccountUsedByThisTest.UserAccount, PasswordOfTheAccountInTheseTests, activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[0]);

        Assert.True(resultOfTheDeactivationOfTheTwoFactor.IsTwoFactorDeactivated);
        Assert.False(activatedAccountUsedByThisTest.UserAccount.IsTwoFactorEnabled);
    }

    // Le même message pour les deux champs : une session volée ne sert pas à tester des mots de passe un par un.
    [Fact]
    public async Task WrongPasswordChangesNothingAndGivesTheSameMessageAsAWrongCode()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();

        var resultOfTheRequestWithTheWrongPassword = await DeactivateWithThisPasswordAndCodeAsync(
            activatedAccountUsedByThisTest.UserAccount,
            WrongPasswordTypedInTheseTests,
            servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));
        var resultOfTheRequestWithTheWrongCode = await DeactivateWithThisPasswordAndCodeAsync(
            activatedAccountUsedByThisTest.UserAccount,
            PasswordOfTheAccountInTheseTests,
            servicesWithFakeToolsOfTheseTests.FindCodeThatIsWrongNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));

        Assert.False(resultOfTheRequestWithTheWrongPassword.IsTwoFactorDeactivated);
        Assert.Equal(TwoFactorDeactivationService.ErrorMessageForWrongPasswordOrCode, resultOfTheRequestWithTheWrongPassword.WrongPasswordOrCodeErrorMessage);
        Assert.Equal(resultOfTheRequestWithTheWrongPassword.WrongPasswordOrCodeErrorMessage, resultOfTheRequestWithTheWrongCode.WrongPasswordOrCodeErrorMessage);
        CheckThatTheAccountIsStillActivated(activatedAccountUsedByThisTest);
        Assert.Equal(2, servicesWithFakeToolsOfTheseTests.CountSecurityEventsInTheJournal(SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Failure));
    }

    // Le bon code envoyé avec un mauvais mot de passe reste utilisable : le mot de passe est vérifié d'abord.
    [Fact]
    public async Task WrongPasswordDoesNotConsumeTheCode()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var codeShownOnThePhoneInThisTest = servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication);
        await DeactivateWithThisPasswordAndCodeAsync(activatedAccountUsedByThisTest.UserAccount, WrongPasswordTypedInTheseTests, codeShownOnThePhoneInThisTest);

        var resultOfTheRequestWithTheRightPassword = await DeactivateWithThisPasswordAndCodeAsync(activatedAccountUsedByThisTest.UserAccount, PasswordOfTheAccountInTheseTests, codeShownOnThePhoneInThisTest);

        Assert.True(resultOfTheRequestWithTheRightPassword.IsTwoFactorDeactivated);
    }

    [Fact]
    public async Task WrongPasswordDoesNotCountAsALoginAttemptOfTheAccount()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();

        await DeactivateWithThisPasswordAndCodeAsync(activatedAccountUsedByThisTest.UserAccount, WrongPasswordTypedInTheseTests, "123456");

        Assert.Equal(0, activatedAccountUsedByThisTest.UserAccount.FailedLoginAttemptCount);
    }

    [Fact]
    public async Task PasswordTypedWithDecomposedAccentIsAccepted()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        activatedAccountUsedByThisTest.UserAccount.PasswordHash = FakePasswordHasher.PrefixOfTheFakePasswordHashes + "Mot-De-Passé-1";

        var resultOfTheDeactivationOfTheTwoFactor = await DeactivateWithThisPasswordAndCodeAsync(activatedAccountUsedByThisTest.UserAccount, "Mot-De-Passé-1", activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[0]);

        Assert.True(resultOfTheDeactivationOfTheTwoFactor.IsTwoFactorDeactivated);
    }

    [Fact]
    public async Task AccountWithoutTwoFactorIsRefusedWithoutCheckingThePassword()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();

        var resultOfTheDeactivationOfTheTwoFactor = await DeactivateWithThisPasswordAndCodeAsync(accountUsedByThisTwoFactorTest, PasswordOfTheAccountInTheseTests, "123456");

        Assert.False(resultOfTheDeactivationOfTheTwoFactor.IsTwoFactorDeactivated);
        Assert.Equal(TwoFactorDeactivationService.ErrorMessageForTwoFactorNotEnabled, resultOfTheDeactivationOfTheTwoFactor.NotEnabledErrorMessage);
        Assert.Equal(0, servicesWithFakeToolsOfTheseTests.HasherThatCountsTheComparedPasswords.NumberOfPasswordsCompared);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
    }

    [Fact]
    public async Task AccountEffacedJustBeforeTheRequestChangesNothing()
    {
        var resultOfTheDeactivationOfTheTwoFactor = await servicesWithFakeToolsOfTheseTests.CreateTwoFactorDeactivationServiceWithFakeTools().DeactivateTwoFactorWithPasswordAndCodeOfTheUserAsync(Guid.NewGuid(), PasswordOfTheAccountInTheseTests, "123456", IpAddressOfTheVisitorInTheseTests);

        Assert.Equal(TwoFactorDeactivationService.ErrorMessageForTwoFactorNotEnabled, resultOfTheDeactivationOfTheTwoFactor.NotEnabledErrorMessage);
    }

    // SS §2 : un code qui vient de servir à se connecter ne peut pas servir à désactiver.
    [Fact]
    public async Task CodeJustUsedToLogInCannotDeactivate()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var codeShownOnThePhoneInThisTest = servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication);
        await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync(), codeShownOnThePhoneInThisTest);

        var resultOfTheDeactivationOfTheTwoFactor = await DeactivateWithThisPasswordAndCodeAsync(activatedAccountUsedByThisTest.UserAccount, PasswordOfTheAccountInTheseTests, codeShownOnThePhoneInThisTest);

        Assert.Equal(TwoFactorDeactivationService.ErrorMessageForWrongPasswordOrCode, resultOfTheDeactivationOfTheTwoFactor.WrongPasswordOrCodeErrorMessage);
        CheckThatTheAccountIsStillActivated(activatedAccountUsedByThisTest);
    }

    // GR Auth : en réactivant, il reçoit 10 nouveaux codes. L'ancien téléphone et les anciens codes ne valent plus rien.
    [Fact]
    public async Task ReactivatingGivesTenNewBackupCodesAndTheOldProofsNoLongerWork()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        await DeactivateWithThisPasswordAndCodeAsync(activatedAccountUsedByThisTest.UserAccount, PasswordOfTheAccountInTheseTests, activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[0]);

        var resultOfTheStartOfTheNewActivation = await servicesWithFakeToolsOfTheseTests.CreateTwoFactorActivationServiceWithFakeTools().StartActivationWithThePasswordOfTheUserAsync(activatedAccountUsedByThisTest.UserAccount.Id, PasswordOfTheAccountInTheseTests, IpAddressOfTheVisitorInTheseTests);
        var newSecretOfTheAuthenticatorApplication = resultOfTheStartOfTheNewActivation.SecretOfTheAuthenticatorApplication!;
        var resultOfTheEndOfTheNewActivation = await servicesWithFakeToolsOfTheseTests.CreateTwoFactorActivationServiceWithFakeTools().FinishActivationWithTheFirstCodeOfTheUserAsync(
            activatedAccountUsedByThisTest.UserAccount.Id,
            servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(newSecretOfTheAuthenticatorApplication),
            IpAddressOfTheVisitorInTheseTests);

        Assert.NotEqual(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication, newSecretOfTheAuthenticatorApplication);
        Assert.Equal(10, resultOfTheEndOfTheNewActivation.BackupCodesToGiveOnlyOnce!.Count);
        Assert.Equal(10, servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes.Count);

        servicesWithFakeToolsOfTheseTests.WaitForTheNextCodeOnThePhone();
        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        var resultOfTheLoginWithTheOldPhone = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(activatedAccountUsedByThisTest.SecretOfTheAuthenticatorApplication));
        var resultWithTheOldBackupCode = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[1]);

        Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, resultOfTheLoginWithTheOldPhone.WrongCodeErrorMessage);
        Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, resultWithTheOldBackupCode.WrongCodeErrorMessage);

        var resultOfTheLoginWithTheNewPhone = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(newSecretOfTheAuthenticatorApplication));
        Assert.True(resultOfTheLoginWithTheNewPhone.IsLoggedIn);
    }

    // --- Le compte change pendant la vérification

    [Fact]
    public async Task PasswordChangedWhileTheCodeIsCheckedChangesNothing()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var numberOfTheComparisonBeingDone = 0;
        servicesWithFakeToolsOfTheseTests.HasherThatCountsTheComparedPasswords.ActionRunWhenOnePasswordIsCompared = () =>
        {
            numberOfTheComparisonBeingDone = numberOfTheComparisonBeingDone + 1;
            if (numberOfTheComparisonBeingDone == 2)
            {
                activatedAccountUsedByThisTest.UserAccount.PasswordHash = FakePasswordHasher.PrefixOfTheFakePasswordHashes + "Nouveau-Mot-De-Passe-3";
            }
        };

        var resultOfTheDeactivationOfTheTwoFactor = await DeactivateWithThisPasswordAndCodeAsync(activatedAccountUsedByThisTest.UserAccount, PasswordOfTheAccountInTheseTests, activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[0]);

        Assert.Equal(TwoFactorDeactivationService.ErrorMessageForWrongPasswordOrCode, resultOfTheDeactivationOfTheTwoFactor.WrongPasswordOrCodeErrorMessage);
        CheckThatTheAccountIsStillActivated(activatedAccountUsedByThisTest);
    }

    [Fact]
    public async Task SecretChangedWhileTheCodeIsCheckedChangesNothing()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var numberOfTheComparisonBeingDone = 0;
        servicesWithFakeToolsOfTheseTests.HasherThatCountsTheComparedPasswords.ActionRunWhenOnePasswordIsCompared = () =>
        {
            numberOfTheComparisonBeingDone = numberOfTheComparisonBeingDone + 1;
            if (numberOfTheComparisonBeingDone == 2)
            {
                activatedAccountUsedByThisTest.UserAccount.EncryptedTotpSecret = "secret-du-nouveau-telephone";
            }
        };

        var resultOfTheDeactivationOfTheTwoFactor = await DeactivateWithThisPasswordAndCodeAsync(activatedAccountUsedByThisTest.UserAccount, PasswordOfTheAccountInTheseTests, activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[0]);

        Assert.Equal(TwoFactorDeactivationService.ErrorMessageForWrongPasswordOrCode, resultOfTheDeactivationOfTheTwoFactor.WrongPasswordOrCodeErrorMessage);
        Assert.Equal("secret-du-nouveau-telephone", activatedAccountUsedByThisTest.UserAccount.EncryptedTotpSecret);
    }

    // Un double clic : l'autre requête a déjà tout désactivé.
    [Fact]
    public async Task DeactivationByAnotherRequestWhileTheCodeIsCheckedAnswersNotEnabled()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var numberOfTheComparisonBeingDone = 0;
        servicesWithFakeToolsOfTheseTests.HasherThatCountsTheComparedPasswords.ActionRunWhenOnePasswordIsCompared = () =>
        {
            numberOfTheComparisonBeingDone = numberOfTheComparisonBeingDone + 1;
            if (numberOfTheComparisonBeingDone == 2)
            {
                activatedAccountUsedByThisTest.UserAccount.IsTwoFactorEnabled = false;
                activatedAccountUsedByThisTest.UserAccount.EncryptedTotpSecret = null;
            }
        };

        var resultOfTheDeactivationOfTheTwoFactor = await DeactivateWithThisPasswordAndCodeAsync(activatedAccountUsedByThisTest.UserAccount, PasswordOfTheAccountInTheseTests, activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[0]);

        Assert.Equal(TwoFactorDeactivationService.ErrorMessageForTwoFactorNotEnabled, resultOfTheDeactivationOfTheTwoFactor.NotEnabledErrorMessage);
    }

    // --- Les conflits et les pannes

    [Fact]
    public async Task ConflictsWithOtherRequestsAreRetried()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.NumberOfConflictsBeforeTheDeactivationIsSaved = 9;

        var resultOfTheDeactivationOfTheTwoFactor = await DeactivateWithThisPasswordAndCodeAsync(activatedAccountUsedByThisTest.UserAccount, PasswordOfTheAccountInTheseTests, activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[0]);

        Assert.True(resultOfTheDeactivationOfTheTwoFactor.IsTwoFactorDeactivated);
        Assert.Equal(1, servicesWithFakeToolsOfTheseTests.CountSecurityEventsInTheJournal(SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Success));
    }

    [Fact]
    public async Task TooManyConflictsChangeNothingOnTheAccount()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.NumberOfConflictsBeforeTheDeactivationIsSaved = 10;

        var resultOfTheDeactivationOfTheTwoFactor = await DeactivateWithThisPasswordAndCodeAsync(activatedAccountUsedByThisTest.UserAccount, PasswordOfTheAccountInTheseTests, activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[0]);

        Assert.False(resultOfTheDeactivationOfTheTwoFactor.IsTwoFactorDeactivated);
        CheckThatTheAccountIsStillActivated(activatedAccountUsedByThisTest);
    }

    [Fact]
    public async Task DatabaseFailureDuringTheSaveIsNotHidden()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.DatabaseIsDownWhileSavingTheSettings = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => DeactivateWithThisPasswordAndCodeAsync(activatedAccountUsedByThisTest.UserAccount, PasswordOfTheAccountInTheseTests, activatedAccountUsedByThisTest.BackupCodesInTheirCleanForm[0]));

        CheckThatTheAccountIsStillActivated(activatedAccountUsedByThisTest);
    }
}
