using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Application.TwoFactorServicesWithFakeTools;

namespace WalletFlow.Tests.Application;

// Vérifie l'activation de la double authentification, en deux temps (GR Auth, SS §2 et §11).
public class TwoFactorActivationServiceTests
{
    private readonly TwoFactorServicesWithFakeTools services = new TwoFactorServicesWithFakeTools();

    // Commence l'activation de ce compte avec son bon mot de passe, et renvoie le secret.
    private async Task<string> StartActivationThenGetSecretAsync(User account)
    {
        var result = await services.CreateActivationService().StartAsync(account.Id, AccountPassword, IpAddress);

        return Assert.IsType<TwoFactorActivationStartResult.Started>(result).Secret;
    }

    // Finit l'activation de ce compte avec ce code.
    private async Task<TwoFactorActivationFinishResult> FinishActivationAsync(User account, string code)
    {
        return await services.CreateActivationService().FinishAsync(account.Id, code, IpAddress);
    }

    // --- Le premier temps : le mot de passe donne le secret

    // Tant que le premier code n'est pas donné, rien n'est écrit : un code QR mal scanné n'enferme personne dehors.
    [Fact]
    public async Task RightPasswordGivesTheSecretWithoutChangingTheAccount()
    {
        var account = services.AddAccountWithoutTwoFactor();

        var result = await services.CreateActivationService().StartAsync(account.Id, AccountPassword, IpAddress);

        Assert.IsType<TwoFactorActivationStartResult.Started>(result);
        Assert.Matches("^[A-Z2-7]{32}$", Assert.IsType<TwoFactorActivationStartResult.Started>(result).Secret);
        Assert.Contains("secret=" + Assert.IsType<TwoFactorActivationStartResult.Started>(result).Secret, Assert.IsType<TwoFactorActivationStartResult.Started>(result).SetupLink);
        Assert.Contains("utilisateur%40exemple.test", Assert.IsType<TwoFactorActivationStartResult.Started>(result).SetupLink);
        Assert.False(account.IsTwoFactorEnabled);
        Assert.Null(account.EncryptedTotpSecret);
        Assert.Empty(services.TwoFactorRepository.SavedBackupCodes);
        Assert.Empty(services.SecurityEventRepository.SavedSecurityEvents);
    }

    [Fact]
    public async Task WrongPasswordGivesNoSecretAndIsWrittenInTheJournal()
    {
        var account = services.AddAccountWithoutTwoFactor();

        var result = await services.CreateActivationService().StartAsync(account.Id, WrongPassword, IpAddress);

        Assert.IsType<TwoFactorActivationStartResult.WrongPassword>(result);
        Assert.Null(services.WaitingSecretStore.Find(account.Id));

        var securityEvent = Assert.Single(services.SecurityEventRepository.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.TwoFactorActivation, securityEvent.Type);
        Assert.Equal(SecurityEventResult.Failure, securityEvent.Result);
        Assert.Equal(IpAddress, securityEvent.IpAddress);
        Assert.Equal(account.Id, securityEvent.UserId);
    }

    // Ces essais ne comptent pas pour le blocage : un voleur de session pourrait sinon bloquer le compte de son propriétaire.
    [Fact]
    public async Task WrongPasswordDoesNotCountAsALoginAttemptOfTheAccount()
    {
        var account = services.AddAccountWithoutTwoFactor();

        await services.CreateActivationService().StartAsync(account.Id, WrongPassword, IpAddress);

        Assert.Equal(0, account.FailedLoginAttemptCount);
    }

    // Le même mot de passe, tapé sur un autre appareil, peut arriver écrit autrement (SS §2).
    [Fact]
    public async Task PasswordTypedWithDecomposedAccentIsAccepted()
    {
        var account = services.AddAccountWithoutTwoFactor();
        account.ReplacePasswordHash(FakePasswordHasher.HashPrefix + "Mot-De-Passé-1");

        var result = await services.CreateActivationService().StartAsync(account.Id, "Mot-De-Passé-1", IpAddress);

        Assert.IsType<TwoFactorActivationStartResult.Started>(result);
    }

    [Fact]
    public async Task AccountAlreadyActivatedIsRefusedWithoutCheckingThePassword()
    {
        var activatedAccount = services.AddActivatedAccount();
        var encryptedSecret = activatedAccount.UserAccount.EncryptedTotpSecret;

        var result = await services.CreateActivationService().StartAsync(activatedAccount.UserAccount.Id, AccountPassword, IpAddress);

        Assert.IsType<TwoFactorActivationStartResult.AlreadyEnabled>(result);
        Assert.Equal(0, services.PasswordHasher.VerifiedPasswordCount);
        Assert.Equal(encryptedSecret, activatedAccount.UserAccount.EncryptedTotpSecret);
    }

    [Fact]
    public async Task AccountEffacedJustBeforeTheStartGetsNoSecret()
    {
        var result = await services.CreateActivationService().StartAsync(Guid.NewGuid(), AccountPassword, IpAddress);

        Assert.IsType<TwoFactorActivationStartResult.WrongPassword>(result);
        Assert.Empty(services.SecurityEventRepository.SavedSecurityEvents);
    }

    [Fact]
    public async Task StartingAgainReplacesTheSecretAndTheOldOneNoLongerWorks()
    {
        var account = services.AddAccountWithoutTwoFactor();
        var firstSecret = await StartActivationThenGetSecretAsync(account);
        var secondSecret = await StartActivationThenGetSecretAsync(account);

        Assert.NotEqual(firstSecret, secondSecret);

        var oldSecretResult = await FinishActivationAsync(account, services.CalculateCurrentCode(firstSecret));
        Assert.IsType<TwoFactorActivationFinishResult.WrongCode>(oldSecretResult);

        var newSecretResult = await FinishActivationAsync(account, services.CalculateCurrentCode(secondSecret));
        Assert.IsType<TwoFactorActivationFinishResult.Activated>(newSecretResult);
    }

    // --- Le second temps : le premier code active tout

    [Fact]
    public async Task RightFirstCodeActivatesEverythingAndGivesTenDifferentBackupCodes()
    {
        var account = services.AddAccountWithoutTwoFactor();
        var secret = await StartActivationThenGetSecretAsync(account);

        var result = await FinishActivationAsync(account, services.CalculateCurrentCode(secret));

        Assert.IsType<TwoFactorActivationFinishResult.Activated>(result);
        Assert.True(account.IsTwoFactorEnabled);

        Assert.Equal(10, Assert.IsType<TwoFactorActivationFinishResult.Activated>(result).BackupCodes.Count);
        Assert.Equal(10, Assert.IsType<TwoFactorActivationFinishResult.Activated>(result).BackupCodes.Distinct().Count());
        foreach (var backupCode in Assert.IsType<TwoFactorActivationFinishResult.Activated>(result).BackupCodes)
        {
            Assert.Matches("^[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}$", backupCode);
        }

        var securityEvent = Assert.Single(services.SecurityEventRepository.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.TwoFactorActivation, securityEvent.Type);
        Assert.Equal(SecurityEventResult.Success, securityEvent.Result);
        Assert.Equal(IpAddress, securityEvent.IpAddress);
        Assert.Equal(account.Id, securityEvent.UserId);
    }

    // SS §2 : le secret est chiffré, et il se relit avec la clé et l'identifiant du compte.
    [Fact]
    public async Task SecretIsSavedEncryptedAndCanBeReadBackWithTheKey()
    {
        var account = services.AddAccountWithoutTwoFactor();
        var secret = await StartActivationThenGetSecretAsync(account);

        await FinishActivationAsync(account, services.CalculateCurrentCode(secret));

        Assert.DoesNotContain(secret, account.EncryptedTotpSecret);
        Assert.Equal(secret, services.SecretProtector.Decrypt(account.EncryptedTotpSecret!, account.Id));
        Assert.True(account.EncryptedTotpSecret!.Length <= 255);
    }

    // SS §2 : les codes de secours sont stockés hachés, chacun correspond à ce qui a été donné, et aucun n'a encore servi.
    [Fact]
    public async Task BackupCodesAreSavedHashedForThisAccountAndNotYetUsed()
    {
        var account = services.AddAccountWithoutTwoFactor();
        var secret = await StartActivationThenGetSecretAsync(account);

        var result = await FinishActivationAsync(account, services.CalculateCurrentCode(secret));

        var backupCodes = services.TwoFactorRepository.SavedBackupCodes;
        Assert.Equal(10, backupCodes.Count);

        for (var position = 0; position < 10; position++)
        {
            var cleanedCode = TwoFactorAuthenticationRules.CleanCode(Assert.IsType<TwoFactorActivationFinishResult.Activated>(result).BackupCodes[position]);
            Assert.Equal(FakeBackupCodeHasher.CreateFakeHash(cleanedCode, account.Id), backupCodes[position].CodeHash);
            Assert.Equal(account.Id, backupCodes[position].UserId);
            Assert.Null(backupCodes[position].UsedAt);
        }
    }

    [Fact]
    public async Task SecretWaitingInMemoryIsForgottenOnceActivated()
    {
        var account = services.AddAccountWithoutTwoFactor();
        var secret = await StartActivationThenGetSecretAsync(account);

        await FinishActivationAsync(account, services.CalculateCurrentCode(secret));

        Assert.Null(services.WaitingSecretStore.Find(account.Id));
    }

    [Fact]
    public async Task WrongFirstCodeActivatesNothingAndCanBeRetried()
    {
        var account = services.AddAccountWithoutTwoFactor();
        var secret = await StartActivationThenGetSecretAsync(account);

        var wrongCodeResult = await FinishActivationAsync(account, services.FindWrongCode(secret));

        Assert.IsType<TwoFactorActivationFinishResult.WrongCode>(wrongCodeResult);
        Assert.False(account.IsTwoFactorEnabled);
        Assert.Null(account.EncryptedTotpSecret);
        Assert.Empty(services.TwoFactorRepository.SavedBackupCodes);
        Assert.Equal(1, services.CountSecurityEvents(SecurityEventType.TwoFactorActivation, SecurityEventResult.Failure));

        var rightCodeResult = await FinishActivationAsync(account, services.CalculateCurrentCode(secret));
        Assert.IsType<TwoFactorActivationFinishResult.Activated>(rightCodeResult);
    }

    // Le compte n'a pas encore de code de secours : un texte de cette forme ne doit lancer aucun Argon2.
    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("ABCDE-FGHJK")]
    [InlineData("١٢٣٤٥٦")]
    public async Task TextThatIsNotAnAuthenticatorCodeIsRefusedWithoutAnyHash(string text)
    {
        var account = services.AddAccountWithoutTwoFactor();
        await StartActivationThenGetSecretAsync(account);
        var comparisonCountBefore = services.PasswordHasher.VerifiedPasswordCount;

        var result = await FinishActivationAsync(account, text);

        Assert.IsType<TwoFactorActivationFinishResult.WrongCode>(result);
        Assert.Equal(comparisonCountBefore, services.PasswordHasher.VerifiedPasswordCount);
        Assert.Equal(0, services.BackupCodeHasher.HashedCodeCount);
    }

    // SS §2 : le premier code a déjà servi. Il ne peut pas servir à se connecter ni à désactiver juste après.
    [Fact]
    public async Task FirstCodeCannotBeUsedAgainToLogIn()
    {
        var account = services.AddAccountWithoutTwoFactor();
        var secret = await StartActivationThenGetSecretAsync(account);
        var firstCode = services.CalculateCurrentCode(secret);
        await FinishActivationAsync(account, firstCode);

        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        var result = await services.FinishLoginAsync(waitingLoginToken, firstCode);

        Assert.IsType<TwoFactorLoginResult.WrongCode>(result);
    }

    [Fact]
    public async Task FinishingWithoutAnyStartAsksToRestart()
    {
        var account = services.AddAccountWithoutTwoFactor();

        var result = await FinishActivationAsync(account, "123456");

        Assert.IsType<TwoFactorActivationFinishResult.ActivationMustRestart>(result);
        Assert.False(account.IsTwoFactorEnabled);
    }

    [Fact]
    public async Task SecretWaitingForTheFirstCodeStillWorksJustBeforeTheEndOfTheTenMinutes()
    {
        var account = services.AddAccountWithoutTwoFactor();
        var secret = await StartActivationThenGetSecretAsync(account);

        services.Clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        var result = await FinishActivationAsync(account, services.CalculateCurrentCode(secret));

        Assert.IsType<TwoFactorActivationFinishResult.Activated>(result);
    }

    [Fact]
    public async Task SecretWaitingForTheFirstCodeEndsExactlyAfterTenMinutes()
    {
        var account = services.AddAccountWithoutTwoFactor();
        var secret = await StartActivationThenGetSecretAsync(account);

        services.Clock.Advance(TimeSpan.FromMinutes(10));
        var result = await FinishActivationAsync(account, services.CalculateCurrentCode(secret));

        Assert.IsType<TwoFactorActivationFinishResult.ActivationMustRestart>(result);
        Assert.False(account.IsTwoFactorEnabled);
    }

    // SS §4 : le secret en attente n'appartient qu'au compte qui l'a demandé.
    [Fact]
    public async Task SecretWaitingForOneAccountCannotActivateAnotherAccount()
    {
        var account = services.AddAccountWithoutTwoFactor();
        var otherAccount = services.AddAccountWithoutTwoFactor("autre-compte@exemple.test");
        var secret = await StartActivationThenGetSecretAsync(account);

        var result = await FinishActivationAsync(otherAccount, services.CalculateCurrentCode(secret));

        Assert.IsType<TwoFactorActivationFinishResult.ActivationMustRestart>(result);
        Assert.False(otherAccount.IsTwoFactorEnabled);
    }

    // --- Les conflits et les pannes

    [Fact]
    public async Task ConflictsWithOtherRequestsAreRetried()
    {
        var account = services.AddAccountWithoutTwoFactor();
        var secret = await StartActivationThenGetSecretAsync(account);
        services.TwoFactorRepository.ActivationConflicts = 9;

        var result = await FinishActivationAsync(account, services.CalculateCurrentCode(secret));

        Assert.IsType<TwoFactorActivationFinishResult.Activated>(result);
        Assert.Equal(10, services.TwoFactorRepository.SavedBackupCodes.Count);
        Assert.Equal(1, services.CountSecurityEvents(SecurityEventType.TwoFactorActivation, SecurityEventResult.Success));
    }

    [Fact]
    public async Task TooManyConflictsAskToRestartAndActivateNothing()
    {
        var account = services.AddAccountWithoutTwoFactor();
        var secret = await StartActivationThenGetSecretAsync(account);
        services.TwoFactorRepository.ActivationConflicts = 10;

        var result = await FinishActivationAsync(account, services.CalculateCurrentCode(secret));

        Assert.IsType<TwoFactorActivationFinishResult.ActivationMustRestart>(result);
        Assert.False(account.IsTwoFactorEnabled);
    }

    // Deux onglets : l'activation de l'autre a réussi juste avant. Ce compte ne reçoit jamais 20 codes de secours.
    [Fact]
    public async Task AccountActivatedByAnotherRequestJustBeforeTheSaveKeepsItsFirstActivation()
    {
        var account = services.AddAccountWithoutTwoFactor();
        var secret = await StartActivationThenGetSecretAsync(account);
        account.ActivateTwoFactor("secret-de-l-autre-onglet");

        var result = await FinishActivationAsync(account, services.CalculateCurrentCode(secret));

        Assert.IsType<TwoFactorActivationFinishResult.ActivationMustRestart>(result);
        Assert.Equal("secret-de-l-autre-onglet", account.EncryptedTotpSecret);
        Assert.Empty(services.TwoFactorRepository.SavedBackupCodes);
    }

    [Fact]
    public async Task AccountEffacedJustBeforeTheSaveActivatesNothing()
    {
        var account = services.AddAccountWithoutTwoFactor();
        var secret = await StartActivationThenGetSecretAsync(account);
        services.AccountRepository.SavedUsers.Remove(account);

        var result = await FinishActivationAsync(account, services.CalculateCurrentCode(secret));

        Assert.IsType<TwoFactorActivationFinishResult.ActivationMustRestart>(result);
        Assert.Empty(services.TwoFactorRepository.SavedBackupCodes);
    }

    // Une panne n'active rien et ne perd pas le secret : le code suivant du téléphone fonctionne, sans rescanner le code QR.
    [Fact]
    public async Task DatabaseFailureDuringTheSaveIsNotHiddenAndTheNextCodeWorksAfterIt()
    {
        var account = services.AddAccountWithoutTwoFactor();
        var secret = await StartActivationThenGetSecretAsync(account);
        services.TwoFactorRepository.FailsWhenSavingSettings = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => FinishActivationAsync(account, services.CalculateCurrentCode(secret)));
        Assert.False(account.IsTwoFactorEnabled);

        services.TwoFactorRepository.FailsWhenSavingSettings = false;
        services.WaitForNextCode();
        var result = await FinishActivationAsync(account, services.CalculateCurrentCode(secret));

        Assert.IsType<TwoFactorActivationFinishResult.Activated>(result);
    }

    [Fact]
    public async Task JournalFailureOnAWrongPasswordIsNotHidden()
    {
        var account = services.AddAccountWithoutTwoFactor();
        services.SecurityEventRepository.IsDown = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => services.CreateActivationService().StartAsync(account.Id, WrongPassword, IpAddress));
    }
}
