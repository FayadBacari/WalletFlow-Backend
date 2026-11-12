using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Application.TwoFactorServicesWithFakeTools;

namespace WalletFlow.Tests.Application;

// Vérifie la désactivation de la double authentification (GR Auth, SS §2 et §11).
public class TwoFactorDeactivationServiceTests
{
    private readonly TwoFactorServicesWithFakeTools services = new TwoFactorServicesWithFakeTools();

    // Désactive avec ce mot de passe et ce code.
    private async Task<TwoFactorDeactivationResult> DeactivateAsync(User account, string password, string code)
    {
        return await services.CreateDeactivationService().DeactivateAsync(
            account.Id,
            password,
            code,
            IpAddress);
    }

    // Vérifie que le compte est resté activé, avec son secret et ses 10 codes.
    private void AssertAccountStillActivated(ActivatedAccount activatedAccount)
    {
        Assert.True(activatedAccount.UserAccount.IsTwoFactorEnabled);
        Assert.NotNull(activatedAccount.UserAccount.EncryptedTotpSecret);
        Assert.Equal(10, services.TwoFactorRepository.SavedBackupCodes.Count);
    }

    [Fact]
    public async Task RightPasswordAndCodeEraseTheSecretAndEveryBackupCode()
    {
        var activatedAccount = services.AddActivatedAccount();
        services.TwoFactorRepository.SavedBackupCodes[0].MarkAsUsed(services.Clock.GetUtcNow().UtcDateTime);

        var result = await DeactivateAsync(
            activatedAccount.UserAccount,
            AccountPassword,
            services.CalculateCurrentCode(activatedAccount.Secret));

        Assert.Equal(TwoFactorDeactivationResult.TwoFactorDeactivated, result);
        Assert.False(activatedAccount.UserAccount.IsTwoFactorEnabled);
        Assert.Null(activatedAccount.UserAccount.EncryptedTotpSecret);
        Assert.Empty(services.TwoFactorRepository.SavedBackupCodes);

        var securityEvent = Assert.Single(services.SecurityEventRepository.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.TwoFactorDeactivation, securityEvent.Type);
        Assert.Equal(SecurityEventResult.Success, securityEvent.Result);
        Assert.Equal(IpAddress, securityEvent.IpAddress);
        Assert.Equal(activatedAccount.UserAccount.Id, securityEvent.UserId);
    }

    // Le seul moyen de changer de téléphone après en avoir perdu un.
    [Fact]
    public async Task BackupCodeAlsoDeactivatesTheTwoFactorAuthentication()
    {
        var activatedAccount = services.AddActivatedAccount();

        var result = await DeactivateAsync(activatedAccount.UserAccount, AccountPassword, activatedAccount.BackupCodes[0]);

        Assert.Equal(TwoFactorDeactivationResult.TwoFactorDeactivated, result);
        Assert.False(activatedAccount.UserAccount.IsTwoFactorEnabled);
    }

    // Le même message pour les deux champs : une session volée ne sert pas à tester des mots de passe un par un.
    [Fact]
    public async Task WrongPasswordChangesNothingAndGivesTheSameMessageAsAWrongCode()
    {
        var activatedAccount = services.AddActivatedAccount();

        var wrongPasswordResult = await DeactivateAsync(
            activatedAccount.UserAccount,
            WrongPassword,
            services.CalculateCurrentCode(activatedAccount.Secret));
        var wrongCodeResult = await DeactivateAsync(
            activatedAccount.UserAccount,
            AccountPassword,
            services.FindWrongCode(activatedAccount.Secret));

        Assert.Equal(TwoFactorDeactivationResult.WrongPasswordOrCode, wrongPasswordResult);
        Assert.Equal(wrongPasswordResult, wrongCodeResult);
        AssertAccountStillActivated(activatedAccount);
        Assert.Equal(2, services.CountSecurityEvents(SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Failure));
    }

    // Le bon code envoyé avec un mauvais mot de passe reste utilisable : le mot de passe est vérifié d'abord.
    [Fact]
    public async Task WrongPasswordDoesNotConsumeTheCode()
    {
        var activatedAccount = services.AddActivatedAccount();
        var code = services.CalculateCurrentCode(activatedAccount.Secret);
        await DeactivateAsync(activatedAccount.UserAccount, WrongPassword, code);

        var result = await DeactivateAsync(activatedAccount.UserAccount, AccountPassword, code);

        Assert.Equal(TwoFactorDeactivationResult.TwoFactorDeactivated, result);
    }

    [Fact]
    public async Task WrongPasswordDoesNotCountAsALoginAttemptOfTheAccount()
    {
        var activatedAccount = services.AddActivatedAccount();

        await DeactivateAsync(activatedAccount.UserAccount, WrongPassword, "123456");

        Assert.Equal(0, activatedAccount.UserAccount.FailedLoginAttemptCount);
    }

    [Fact]
    public async Task PasswordTypedWithDecomposedAccentIsAccepted()
    {
        var activatedAccount = services.AddActivatedAccount();
        activatedAccount.UserAccount.ReplacePasswordHash(FakePasswordHasher.HashPrefix + "Mot-De-Passé-1");

        var result = await DeactivateAsync(activatedAccount.UserAccount, "Mot-De-Passe\u0301-1", activatedAccount.BackupCodes[0]);

        Assert.Equal(TwoFactorDeactivationResult.TwoFactorDeactivated, result);
    }

    [Fact]
    public async Task AccountWithoutTwoFactorIsRefusedWithoutCheckingThePassword()
    {
        var account = services.AddAccountWithoutTwoFactor();

        var result = await DeactivateAsync(account, AccountPassword, "123456");

        Assert.Equal(TwoFactorDeactivationResult.TwoFactorNotEnabled, result);
        Assert.Equal(0, services.PasswordHasher.VerifiedPasswordCount);
        Assert.Empty(services.SecurityEventRepository.SavedSecurityEvents);
    }

    [Fact]
    public async Task AccountEffacedJustBeforeTheRequestChangesNothing()
    {
        var result = await services.CreateDeactivationService().DeactivateAsync(Guid.NewGuid(), AccountPassword, "123456", IpAddress);

        Assert.Equal(TwoFactorDeactivationResult.TwoFactorNotEnabled, result);
    }

    // SS §2 : un code qui vient de servir à se connecter ne peut pas servir à désactiver.
    [Fact]
    public async Task CodeJustUsedToLogInCannotDeactivate()
    {
        var activatedAccount = services.AddActivatedAccount();
        var code = services.CalculateCurrentCode(activatedAccount.Secret);
        await services.FinishLoginAsync(await services.LogInThenGetWaitingLoginTokenAsync(), code);

        var result = await DeactivateAsync(activatedAccount.UserAccount, AccountPassword, code);

        Assert.Equal(TwoFactorDeactivationResult.WrongPasswordOrCode, result);
        AssertAccountStillActivated(activatedAccount);
    }

    // GR Auth : en réactivant, il reçoit 10 nouveaux codes. L'ancien téléphone et les anciens codes ne valent plus rien.
    [Fact]
    public async Task ReactivatingGivesTenNewBackupCodesAndTheOldProofsNoLongerWork()
    {
        var activatedAccount = services.AddActivatedAccount();
        await DeactivateAsync(activatedAccount.UserAccount, AccountPassword, activatedAccount.BackupCodes[0]);

        var startResult = await services.CreateActivationService().StartAsync(activatedAccount.UserAccount.Id, AccountPassword, IpAddress);
        var secret = Assert.IsType<TwoFactorActivationStartResult.Started>(startResult).Secret;
        var finishResult = await services.CreateActivationService().FinishAsync(
            activatedAccount.UserAccount.Id,
            services.CalculateCurrentCode(secret),
            IpAddress);

        Assert.NotEqual(activatedAccount.Secret, secret);
        Assert.Equal(10, Assert.IsType<TwoFactorActivationFinishResult.Activated>(finishResult).BackupCodes.Count);
        Assert.Equal(10, services.TwoFactorRepository.SavedBackupCodes.Count);

        services.WaitForNextCode();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        var oldPhoneResult = await services.FinishLoginAsync(waitingLoginToken, services.CalculateCurrentCode(activatedAccount.Secret));
        var oldBackupCodeResult = await services.FinishLoginAsync(waitingLoginToken, activatedAccount.BackupCodes[1]);

        Assert.IsType<TwoFactorLoginResult.WrongCode>(oldPhoneResult);
        Assert.IsType<TwoFactorLoginResult.WrongCode>(oldBackupCodeResult);

        var newPhoneResult = await services.FinishLoginAsync(waitingLoginToken, services.CalculateCurrentCode(secret));
        Assert.IsType<TwoFactorLoginResult.LoggedIn>(newPhoneResult);
    }

    // --- Le compte change pendant la vérification

    [Fact]
    public async Task PasswordChangedWhileTheCodeIsCheckedChangesNothing()
    {
        var activatedAccount = services.AddActivatedAccount();
        services.BackupCodeHasher.OnBackupCodeHashed = () =>
            activatedAccount.UserAccount.ReplacePasswordHash(FakePasswordHasher.HashPrefix + "Nouveau-Mot-De-Passe-3");

        var result = await DeactivateAsync(activatedAccount.UserAccount, AccountPassword, activatedAccount.BackupCodes[0]);

        Assert.Equal(TwoFactorDeactivationResult.WrongPasswordOrCode, result);
        AssertAccountStillActivated(activatedAccount);
    }

    [Fact]
    public async Task SecretChangedWhileTheCodeIsCheckedChangesNothing()
    {
        var activatedAccount = services.AddActivatedAccount();
        services.BackupCodeHasher.OnBackupCodeHashed = () =>
            activatedAccount.UserAccount.ActivateTwoFactor("secret-du-nouveau-telephone");

        var result = await DeactivateAsync(activatedAccount.UserAccount, AccountPassword, activatedAccount.BackupCodes[0]);

        Assert.Equal(TwoFactorDeactivationResult.WrongPasswordOrCode, result);
        Assert.Equal("secret-du-nouveau-telephone", activatedAccount.UserAccount.EncryptedTotpSecret);
    }

    // Un double clic : l'autre requête a déjà tout désactivé.
    [Fact]
    public async Task DeactivationByAnotherRequestWhileTheCodeIsCheckedAnswersNotEnabled()
    {
        var activatedAccount = services.AddActivatedAccount();
        services.BackupCodeHasher.OnBackupCodeHashed = () =>
            activatedAccount.UserAccount.DeactivateTwoFactor();

        var result = await DeactivateAsync(activatedAccount.UserAccount, AccountPassword, activatedAccount.BackupCodes[0]);

        Assert.Equal(TwoFactorDeactivationResult.TwoFactorNotEnabled, result);
    }

    // --- Les conflits et les pannes

    [Fact]
    public async Task ConflictsWithOtherRequestsAreRetried()
    {
        var activatedAccount = services.AddActivatedAccount();
        services.TwoFactorRepository.DeactivationConflicts = 9;

        var result = await DeactivateAsync(activatedAccount.UserAccount, AccountPassword, activatedAccount.BackupCodes[0]);

        Assert.Equal(TwoFactorDeactivationResult.TwoFactorDeactivated, result);
        Assert.Equal(1, services.CountSecurityEvents(SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Success));
    }

    [Fact]
    public async Task TooManyConflictsChangeNothingOnTheAccount()
    {
        var activatedAccount = services.AddActivatedAccount();
        services.TwoFactorRepository.DeactivationConflicts = 10;

        var result = await DeactivateAsync(activatedAccount.UserAccount, AccountPassword, activatedAccount.BackupCodes[0]);

        Assert.NotEqual(TwoFactorDeactivationResult.TwoFactorDeactivated, result);
        AssertAccountStillActivated(activatedAccount);
    }

    [Fact]
    public async Task DatabaseFailureDuringTheSaveIsNotHidden()
    {
        var activatedAccount = services.AddActivatedAccount();
        services.TwoFactorRepository.FailsWhenSavingSettings = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => DeactivateAsync(activatedAccount.UserAccount, AccountPassword, activatedAccount.BackupCodes[0]));

        AssertAccountStillActivated(activatedAccount);
    }
}
