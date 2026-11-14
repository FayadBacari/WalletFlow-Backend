using System.Security.Cryptography;
using WalletFlow.Application.AccountRecovery;
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
    private readonly TwoFactorServicesWithFakeTools services = new TwoFactorServicesWithFakeTools();

    // --- Le mot de passe ne suffit pas

    [Fact]
    public async Task RightPasswordAloneOpensNoSessionAndKeepsTheAttemptCounted()
    {
        var activatedAccount = services.AddActivatedAccount();

        var result = await services.LogInAsync(AccountPassword);

        Assert.IsType<AccountLoginResult.WaitingForTwoFactorCode>(result);
        Assert.Empty(services.SessionRepository.SavedSessions);
        Assert.Empty(services.SessionRepository.SavedDevices);
        Assert.Empty(services.SecurityEventRepository.SavedSecurityEvents);
        Assert.Equal(1, activatedAccount.UserAccount.FailedLoginAttemptCount);
    }

    [Fact]
    public async Task WrongPasswordOnAnActivatedAccountNeverAsksForTheCode()
    {
        services.AddActivatedAccount();

        var result = await services.LogInAsync(WrongPassword);

        Assert.IsType<AccountLoginResult.WrongCredentials>(result);
    }

    [Fact]
    public async Task AccountWithoutTwoFactorStillLogsInWithItsPasswordAlone()
    {
        services.AddAccountWithoutTwoFactor();

        var result = await services.LogInAsync(AccountPassword);

        Assert.IsType<AccountLoginResult.LoggedIn>(result);
    }

    // Sans le contrôle du secret, une connexion lancée juste avant l'activation s'ouvrirait sans code.
    [Fact]
    public async Task TwoFactorActivatedDuringThePasswordCheckRefusesTheLoginWithoutCode()
    {
        var account = services.AddAccountWithoutTwoFactor();
        services.PasswordHasher.OnPasswordVerified = () =>
        {
            account.ActivateTwoFactor("secret-chiffre-par-une-autre-requete");
        };

        var result = await services.LogInAsync(AccountPassword);

        Assert.IsType<AccountLoginResult.WrongCredentials>(result);
        Assert.Empty(services.SessionRepository.SavedSessions);
    }

    // --- Le bon code

    // Seul le bon code rend la connexion réussie : les compteurs repartent alors à zéro (GR Auth).
    [Fact]
    public async Task RightCodeOpensTheSessionAndResetsEveryLoginCounter()
    {
        var activatedAccount = services.AddActivatedAccount(lockoutCount: 3);
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();

        var result = await services.FinishLoginAsync(
            waitingLoginToken,
            services.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<TwoFactorLoginResult.LoggedIn>(result);
        var session = Assert.Single(services.SessionRepository.SavedSessions);
        Assert.Equal(activatedAccount.UserAccount.Id, session.UserId);
        Assert.Equal(services.TokenGenerator.HashToken(Assert.IsType<TwoFactorLoginResult.LoggedIn>(result).SessionToken), session.Id);
        Assert.Single(services.SessionRepository.SavedDevices);
        Assert.Equal(0, activatedAccount.UserAccount.FailedLoginAttemptCount);
        Assert.Equal(0, activatedAccount.UserAccount.LockoutCount);
        Assert.Null(activatedAccount.UserAccount.LockoutEndsAt);

        var securityEvent = Assert.Single(services.SecurityEventRepository.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.Login, securityEvent.Type);
        Assert.Equal(SecurityEventResult.Success, securityEvent.Result);
        Assert.Equal(IpAddress, securityEvent.IpAddress);
        Assert.Equal(activatedAccount.UserAccount.Id, securityEvent.UserId);
    }

    [Fact]
    public async Task WaitingLoginCannotServeASecondTimeAfterASuccessfulLogin()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        await services.FinishLoginAsync(waitingLoginToken, services.CalculateCurrentCode(activatedAccount.Secret));
        services.WaitForNextCode();

        var result = await services.FinishLoginAsync(waitingLoginToken, services.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<TwoFactorLoginResult.LoginMustRestart>(result);
        Assert.Single(services.SessionRepository.SavedSessions);
    }

    // Le téléphone et le serveur n'ont jamais tout à fait la même heure (SS §2).
    [Theory]
    [InlineData(-30)]
    [InlineData(0)]
    [InlineData(30)]
    public async Task CodeOfTheCurrentPreviousOrNextPeriodIsAccepted(int driftSeconds)
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        var code = TwoFactorTestTools.CalculateCodeAt(
            activatedAccount.Secret,
            services.Clock.GetUtcNow().AddSeconds(driftSeconds));

        var result = await services.FinishLoginAsync(waitingLoginToken, code);

        Assert.IsType<TwoFactorLoginResult.LoggedIn>(result);
    }

    [Theory]
    [InlineData(-60)]
    [InlineData(60)]
    public async Task CodeOfAPeriodTooFarAwayIsRefused(int driftSeconds)
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        var code = TwoFactorTestTools.CalculateCodeAt(
            activatedAccount.Secret,
            services.Clock.GetUtcNow().AddSeconds(driftSeconds));

        var result = await services.FinishLoginAsync(waitingLoginToken, code);

        Assert.IsType<TwoFactorLoginResult.WrongCode>(result);
    }

    // Les applications affichent souvent le code avec un espace au milieu.
    [Fact]
    public async Task CodeTypedWithASpaceInTheMiddleIsAccepted()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        var code = services.CalculateCurrentCode(activatedAccount.Secret);

        var result = await services.FinishLoginAsync(waitingLoginToken, code.Substring(0, 3) + " " + code.Substring(3));

        Assert.IsType<TwoFactorLoginResult.LoggedIn>(result);
    }

    // SS §2 : quelqu'un qui a vu passer un code ne peut pas s'en resservir, même pendant qu'il reste valable.
    [Fact]
    public async Task CodeAlreadyUsedToLogInIsRefusedUntilThePhoneShowsANewOne()
    {
        var activatedAccount = services.AddActivatedAccount();
        var code = services.CalculateCurrentCode(activatedAccount.Secret);
        var firstWaitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        await services.FinishLoginAsync(firstWaitingLoginToken, code);

        var secondWaitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        var sameCodeResult = await services.FinishLoginAsync(secondWaitingLoginToken, code);

        Assert.IsType<TwoFactorLoginResult.WrongCode>(sameCodeResult);
        Assert.Single(services.SessionRepository.SavedSessions);

        services.WaitForNextCode();
        var nextCodeResult = await services.FinishLoginAsync(secondWaitingLoginToken, services.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<TwoFactorLoginResult.LoggedIn>(nextCodeResult);
    }

    // Un code plus ancien que le dernier utilisé ne vaut plus rien, même s'il n'a jamais servi.
    [Fact]
    public async Task CodeOlderThanTheLastOneUsedIsRefused()
    {
        var activatedAccount = services.AddActivatedAccount();
        var now = services.Clock.GetUtcNow();
        var firstWaitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        await services.FinishLoginAsync(firstWaitingLoginToken, TwoFactorTestTools.CalculateCodeAt(activatedAccount.Secret, now.AddSeconds(30)));

        var secondWaitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        var result = await services.FinishLoginAsync(secondWaitingLoginToken, TwoFactorTestTools.CalculateCodeAt(activatedAccount.Secret, now));

        Assert.IsType<TwoFactorLoginResult.WrongCode>(result);
    }

    // --- Les mauvais codes

    [Fact]
    public async Task WrongCodeIsRefusedAndWrittenInTheJournalThenTheRightCodeStillWorks()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();

        var wrongCodeResult = await services.FinishLoginAsync(waitingLoginToken, services.FindWrongCode(activatedAccount.Secret));

        Assert.IsType<TwoFactorLoginResult.WrongCode>(wrongCodeResult);
        Assert.Empty(services.SessionRepository.SavedSessions);

        var securityEvent = Assert.Single(services.SecurityEventRepository.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.Login, securityEvent.Type);
        Assert.Equal(SecurityEventResult.Failure, securityEvent.Result);
        Assert.Equal(IpAddress, securityEvent.IpAddress);
        Assert.Equal(activatedAccount.UserAccount.Id, securityEvent.UserId);

        var rightCodeResult = await services.FinishLoginAsync(waitingLoginToken, services.CalculateCurrentCode(activatedAccount.Secret));
        Assert.IsType<TwoFactorLoginResult.LoggedIn>(rightCodeResult);
    }

    // SS §6 : 5 essais, puis il faut recommencer la connexion. Même le bon code ne passe plus.
    [Fact]
    public async Task FifthWrongCodeForcesTheVisitorToRestartEvenWithTheRightCodeAfterwards()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        var wrongCode = services.FindWrongCode(activatedAccount.Secret);

        for (var attemptNumber = 1; attemptNumber <= 4; attemptNumber++)
        {
            var wrongCodeResult = await services.FinishLoginAsync(waitingLoginToken, wrongCode);
            Assert.IsType<TwoFactorLoginResult.WrongCode>(wrongCodeResult);
        }

        var fifthResult = await services.FinishLoginAsync(waitingLoginToken, wrongCode);
        Assert.IsType<TwoFactorLoginResult.LoginMustRestart>(fifthResult);

        var lateResult = await services.FinishLoginAsync(waitingLoginToken, services.CalculateCurrentCode(activatedAccount.Secret));
        Assert.IsType<TwoFactorLoginResult.LoginMustRestart>(lateResult);
        Assert.Empty(services.SessionRepository.SavedSessions);
        Assert.Equal(5, services.CountSecurityEvents(SecurityEventType.Login, SecurityEventResult.Failure));
    }

    [Fact]
    public async Task RightCodeOnTheFifthTryStillOpensTheSession()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        var wrongCode = services.FindWrongCode(activatedAccount.Secret);

        for (var attemptNumber = 1; attemptNumber <= 4; attemptNumber++)
        {
            await services.FinishLoginAsync(waitingLoginToken, wrongCode);
        }

        var result = await services.FinishLoginAsync(waitingLoginToken, services.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<TwoFactorLoginResult.LoggedIn>(result);
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
    public async Task TextWithoutTheFormatOfACodeIsRefusedWithoutAnyCalculation(string text)
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        activatedAccount.UserAccount.ActivateTwoFactor("secret-illisible");
        var comparisonCountBefore = services.PasswordHasher.VerifiedPasswordCount;
        var hashedCountBefore = services.BackupCodeHasher.HashedCodeCount;

        var result = await services.FinishLoginAsync(waitingLoginToken, text);

        Assert.IsType<TwoFactorLoginResult.WrongCode>(result);
        Assert.Equal(comparisonCountBefore, services.PasswordHasher.VerifiedPasswordCount);
        Assert.Equal(hashedCountBefore, services.BackupCodeHasher.HashedCodeCount);
    }

    // Un mauvais code de secours ne coûte qu'un seul calcul, quel que soit le nombre de codes qui restent (SS §2).
    [Fact]
    public async Task WrongBackupCodeCostsOnlyOneHashWhateverTheNumberOfCodesLeft()
    {
        services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        var hashedCountBefore = services.BackupCodeHasher.HashedCodeCount;

        var result = await services.FinishLoginAsync(waitingLoginToken, "ABCDE-FGHJK");

        Assert.IsType<TwoFactorLoginResult.WrongCode>(result);
        Assert.Equal(hashedCountBefore + 1, services.BackupCodeHasher.HashedCodeCount);
    }

    // Le dernier des 10 codes est trouvé avec un seul calcul, lui aussi.
    [Fact]
    public async Task LastBackupCodeOfTheListIsFoundWithOnlyOneHash()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        var hashedCountBefore = services.BackupCodeHasher.HashedCodeCount;

        var result = await services.FinishLoginAsync(waitingLoginToken, activatedAccount.BackupCodes[9]);

        Assert.IsType<TwoFactorLoginResult.LoggedIn>(result);
        Assert.Equal(hashedCountBefore + 1, services.BackupCodeHasher.HashedCodeCount);
    }

    [Fact]
    public async Task EnormousTextIsRefusedLikeAnyWrongCode()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();

        var result = await services.FinishLoginAsync(waitingLoginToken, new string('1', 100000));

        Assert.IsType<TwoFactorLoginResult.WrongCode>(result);
        Assert.Empty(services.SessionRepository.SavedSessions);
    }

    // --- Une attente de 5 minutes au plus

    [Fact]
    public async Task WaitingLoginStillWorksJustBeforeTheEndOfTheFiveMinutes()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();

        services.Clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        var result = await services.FinishLoginAsync(waitingLoginToken, services.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<TwoFactorLoginResult.LoggedIn>(result);
    }

    [Fact]
    public async Task WaitingLoginEndsExactlyAfterFiveMinutes()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();

        services.Clock.Advance(TimeSpan.FromMinutes(5));
        var result = await services.FinishLoginAsync(waitingLoginToken, services.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<TwoFactorLoginResult.LoginMustRestart>(result);
        Assert.Empty(services.SessionRepository.SavedSessions);
    }

    // Rien n'est écrit au journal : personne ne doit pouvoir le remplir sans avoir donné le bon mot de passe.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("jeton-inconnu")]
    public async Task MissingOrUnknownTokenAsksToRestartWithoutWritingInTheJournal(string? waitingLoginToken)
    {
        services.AddActivatedAccount();

        var result = await services.FinishLoginAsync(waitingLoginToken, "123456");

        Assert.IsType<TwoFactorLoginResult.LoginMustRestart>(result);
        Assert.Empty(services.SecurityEventRepository.SavedSecurityEvents);
    }

    // --- Les codes de secours

    [Fact]
    public async Task BackupCodeOpensTheSessionAndIsMarkedAsUsed()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        var backupCode = activatedAccount.BackupCodes[3];

        var result = await services.FinishLoginAsync(waitingLoginToken, TwoFactorAuthenticationRules.FormatBackupCode(backupCode));

        Assert.IsType<TwoFactorLoginResult.LoggedIn>(result);
        var backupCodes = await services.TwoFactorRepository.FindUnusedBackupCodesAsync(activatedAccount.UserAccount.Id);
        Assert.Equal(9, backupCodes.Count);
        var usedCode = services.TwoFactorRepository.SavedBackupCodes[3];
        Assert.Equal(services.Clock.GetUtcNow().UtcDateTime, usedCode.UsedAt);
    }

    [Fact]
    public async Task BackupCodeAlreadyUsedIsRefusedAtTheNextLogin()
    {
        var activatedAccount = services.AddActivatedAccount();
        var backupCode = activatedAccount.BackupCodes[0];
        await services.FinishLoginAsync(await services.LogInThenGetWaitingLoginTokenAsync(), backupCode);

        var secondUseResult = await services.FinishLoginAsync(await services.LogInThenGetWaitingLoginTokenAsync(), backupCode);

        Assert.IsType<TwoFactorLoginResult.WrongCode>(secondUseResult);
        Assert.Single(services.SessionRepository.SavedSessions);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task BackupCodeIsAcceptedInLowercaseAndWithoutItsDash(bool codeWithDash, bool lowercaseCode)
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        var code = activatedAccount.BackupCodes[0];

        if (codeWithDash)
        {
            code = TwoFactorAuthenticationRules.FormatBackupCode(code);
        }

        if (lowercaseCode)
        {
            code = code.ToLowerInvariant();
        }

        var result = await services.FinishLoginAsync(waitingLoginToken, code);

        Assert.IsType<TwoFactorLoginResult.LoggedIn>(result);
    }

    [Fact]
    public async Task BackupCodeOfAnotherAccountIsRefused()
    {
        services.AddActivatedAccount();
        var otherAccount = services.AddActivatedAccount("autre-compte@exemple.test");
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();

        var result = await services.FinishLoginAsync(waitingLoginToken, otherAccount.BackupCodes[0]);

        Assert.IsType<TwoFactorLoginResult.WrongCode>(result);
        var backupCodes = await services.TwoFactorRepository.FindUnusedBackupCodesAsync(otherAccount.UserAccount.Id);
        Assert.Equal(10, backupCodes.Count);
    }

    // Deux requêtes avec le même code de secours : celle qui arrive après le contrôle de l'autre ne doit rien ouvrir.
    [Fact]
    public async Task BackupCodeUsedByAnotherRequestDuringTheCheckOpensNothing()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        services.BackupCodeHasher.OnBackupCodeHashed = () =>
            services.TwoFactorRepository.SavedBackupCodes[0].MarkAsUsed(services.Clock.GetUtcNow().UtcDateTime);

        var result = await services.FinishLoginAsync(waitingLoginToken, activatedAccount.BackupCodes[0]);

        Assert.IsType<TwoFactorLoginResult.LoginMustRestart>(result);
        Assert.Empty(services.SessionRepository.SavedSessions);
    }

    [Fact]
    public async Task BackupCodeEffacedDuringTheCheckOpensNothing()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        var erasedCode = services.TwoFactorRepository.SavedBackupCodes[0];
        services.BackupCodeHasher.OnBackupCodeHashed = () =>
            services.TwoFactorRepository.SavedBackupCodes.Remove(erasedCode);

        var result = await services.FinishLoginAsync(waitingLoginToken, activatedAccount.BackupCodes[0]);

        Assert.IsType<TwoFactorLoginResult.LoginMustRestart>(result);
        Assert.Empty(services.SessionRepository.SavedSessions);
    }

    // --- Le compte change pendant l'attente

    [Fact]
    public async Task PasswordChangedDuringTheWaitForcesTheVisitorToRestart()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        activatedAccount.UserAccount.ReplacePasswordHash(FakePasswordHasher.HashPrefix + "Nouveau-Mot-De-Passe-3");

        var result = await services.FinishLoginAsync(waitingLoginToken, services.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<TwoFactorLoginResult.LoginMustRestart>(result);
        Assert.Empty(services.SessionRepository.SavedSessions);
        Assert.Equal(1, services.CountSecurityEvents(SecurityEventType.Login, SecurityEventResult.Failure));
    }

    [Fact]
    public async Task PasswordChangedWhileTheCodeIsCheckedOpensNothing()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        services.BackupCodeHasher.OnBackupCodeHashed = () =>
            activatedAccount.UserAccount.ReplacePasswordHash(FakePasswordHasher.HashPrefix + "Nouveau-Mot-De-Passe-3");

        var result = await services.FinishLoginAsync(waitingLoginToken, activatedAccount.BackupCodes[0]);

        Assert.IsType<TwoFactorLoginResult.LoginMustRestart>(result);
        Assert.Empty(services.SessionRepository.SavedSessions);
    }

    [Fact]
    public async Task TwoFactorDeactivatedDuringTheWaitForcesTheVisitorToRestart()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        var code = services.CalculateCurrentCode(activatedAccount.Secret);
        activatedAccount.UserAccount.DeactivateTwoFactor();

        var result = await services.FinishLoginAsync(waitingLoginToken, code);

        Assert.IsType<TwoFactorLoginResult.LoginMustRestart>(result);
        Assert.Empty(services.SessionRepository.SavedSessions);
    }

    // Une connexion abandonnée ne revient pas, même si le compte redevient comme avant : il faut redonner le mot de passe.
    [Fact]
    public async Task AbandonedWaitingLoginDoesNotComeBackWhenTheTwoFactorIsActivatedAgain()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        var encryptedSecret = activatedAccount.UserAccount.EncryptedTotpSecret;
        Assert.NotNull(encryptedSecret);
        activatedAccount.UserAccount.DeactivateTwoFactor();
        await services.FinishLoginAsync(waitingLoginToken, "123456");

        activatedAccount.UserAccount.ActivateTwoFactor(encryptedSecret);
        var result = await services.FinishLoginAsync(waitingLoginToken, services.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<TwoFactorLoginResult.LoginMustRestart>(result);
        Assert.Empty(services.SessionRepository.SavedSessions);
    }

    // Le secret qui vient de servir à vérifier le code ne vaut plus : le propriétaire vient peut-être de changer de téléphone.
    [Fact]
    public async Task SecretChangedWhileTheCodeIsCheckedOpensNothing()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        services.BackupCodeHasher.OnBackupCodeHashed = () =>
            activatedAccount.UserAccount.ActivateTwoFactor("secret-du-nouveau-telephone");

        var result = await services.FinishLoginAsync(waitingLoginToken, activatedAccount.BackupCodes[0]);

        Assert.IsType<TwoFactorLoginResult.LoginMustRestart>(result);
        Assert.Empty(services.SessionRepository.SavedSessions);
    }

    // Le compte a disparu : le journal ne peut plus citer son identifiant, la vraie base refuserait l'écriture.
    [Fact]
    public async Task AccountEffacedDuringTheWaitForcesTheVisitorToRestartWithoutItsIdentifierInTheJournal()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        services.AccountRepository.SavedUsers.Remove(activatedAccount.UserAccount);

        var result = await services.FinishLoginAsync(waitingLoginToken, "123456");

        Assert.IsType<TwoFactorLoginResult.LoginMustRestart>(result);
        var securityEvent = Assert.Single(services.SecurityEventRepository.SavedSecurityEvents);
        Assert.Equal(SecurityEventResult.Failure, securityEvent.Result);
        Assert.Null(securityEvent.UserId);
    }

    // GR Auth : une récupération ne désactive pas la double authentification, et le pirate d'une boîte mail ne peut pas s'en servir pour entrer.
    [Fact]
    public async Task PasswordRecoveryKeepsTheTwoFactorAndTheNextLoginStillAsksForTheCode()
    {
        var activatedAccount = services.AddActivatedAccount();
        var recoveryToken = services.TokenGenerator.GenerateToken();
        services.AccountRepository.SavedEmailTokens.Add(new EmailToken
        {
            Type = EmailTokenType.PasswordRecovery,
            TokenHash = recoveryToken.Hash,
            ExpiresAt = services.Clock.GetUtcNow().UtcDateTime.AddMinutes(30),
            UserId = activatedAccount.UserAccount.Id
        });
        var encryptedSecret = activatedAccount.UserAccount.EncryptedTotpSecret;

        var recoveryResult = await services.CreatePasswordResetService().ResetAsync(recoveryToken.Token, "Nouveau-Mot-De-Passe-3", IpAddress);

        Assert.IsType<PasswordResetResult.PasswordChanged>(recoveryResult);
        Assert.True(activatedAccount.UserAccount.IsTwoFactorEnabled);
        Assert.Equal(encryptedSecret, activatedAccount.UserAccount.EncryptedTotpSecret);
        Assert.Equal(10, services.TwoFactorRepository.SavedBackupCodes.Count);

        var loginResult = await services.LogInAsync("Nouveau-Mot-De-Passe-3");
        Assert.IsType<AccountLoginResult.WaitingForTwoFactorCode>(loginResult);
    }

    // --- Le blocage du compte

    // GR Auth : seul le bon code remet à zéro. Un voleur du mot de passe finit bloqué, et le propriétaire reçoit un seul e-mail.
    [Fact]
    public async Task FifthLoginWithTheRightPasswordLocksTheAccountAndWarnsTheOwnerOnce()
    {
        var activatedAccount = services.AddActivatedAccount();

        for (var attemptNumber = 1; attemptNumber <= 4; attemptNumber++)
        {
            var attemptResult = await services.LogInAsync(AccountPassword);
            Assert.IsType<AccountLoginResult.WaitingForTwoFactorCode>(attemptResult);
            Assert.Null(activatedAccount.UserAccount.LockoutEndsAt);
        }

        Assert.Empty(services.EmailQueue.AddedRequests);

        var fifthResult = await services.LogInAsync(AccountPassword);

        Assert.IsType<AccountLoginResult.WaitingForTwoFactorCode>(fifthResult);
        Assert.Equal(services.Clock.GetUtcNow().UtcDateTime.AddMinutes(15), activatedAccount.UserAccount.LockoutEndsAt);
        Assert.Equal(1, activatedAccount.UserAccount.LockoutCount);
        Assert.Equal(1, services.CountSecurityEvents(SecurityEventType.Lockout, SecurityEventResult.Success));
        var unlockRequest = Assert.IsType<BackgroundEmailRequest.AccountUnlock>(Assert.Single(services.EmailQueue.AddedRequests));
        Assert.Equal(activatedAccount.UserAccount.Id, unlockRequest.AccountId);
    }

    [Fact]
    public async Task LoginDuringTheLockIsRefusedEvenWithTheRightPassword()
    {
        services.AddActivatedAccount();
        for (var attemptNumber = 1; attemptNumber <= 5; attemptNumber++)
        {
            await services.LogInAsync(AccountPassword);
        }

        var result = await services.LogInAsync(AccountPassword);

        Assert.IsType<AccountLoginResult.WrongCredentials>(result);
    }

    // Le vrai propriétaire a le mot de passe et le code : son 5e essai le fait entrer, et le blocage disparaît.
    [Fact]
    public async Task RightCodeAfterTheLockOpensTheSessionAndRemovesTheLock()
    {
        var activatedAccount = services.AddActivatedAccount();
        string fifthWaitingLoginToken = "";
        for (var attemptNumber = 1; attemptNumber <= 5; attemptNumber++)
        {
            fifthWaitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        }

        var result = await services.FinishLoginAsync(fifthWaitingLoginToken, services.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<TwoFactorLoginResult.LoggedIn>(result);
        Assert.Null(activatedAccount.UserAccount.LockoutEndsAt);
        Assert.Equal(0, activatedAccount.UserAccount.FailedLoginAttemptCount);
        Assert.Equal(0, activatedAccount.UserAccount.LockoutCount);
    }

    // Une série de mauvais codes ne bloque pas toute seule : c'est de recommencer la connexion qui compte (SS §6).
    [Fact]
    public async Task WrongCodesDoNotCountAsLoginAttemptsOfTheAccount()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        var wrongCode = services.FindWrongCode(activatedAccount.Secret);

        for (var attemptNumber = 1; attemptNumber <= 5; attemptNumber++)
        {
            await services.FinishLoginAsync(waitingLoginToken, wrongCode);
        }

        Assert.Equal(1, activatedAccount.UserAccount.FailedLoginAttemptCount);
    }

    // --- Les pannes

    [Fact]
    public async Task ConflictsWithOtherRequestsAreRetriedAndOpenOneSessionOnly()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        services.SessionRepository.LoginConflicts = 9;

        var result = await services.FinishLoginAsync(waitingLoginToken, services.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<TwoFactorLoginResult.LoggedIn>(result);
        Assert.Single(services.SessionRepository.SavedSessions);
    }

    [Fact]
    public async Task TooManyConflictsAskToRestartAndOpenNothing()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        services.SessionRepository.LoginConflicts = 10;

        var result = await services.FinishLoginAsync(waitingLoginToken, services.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<TwoFactorLoginResult.LoginMustRestart>(result);
        Assert.Empty(services.SessionRepository.SavedSessions);
    }

    // Une panne ne fait croire à personne que la connexion a réussi. Le code est pris, mais le suivant fonctionne, sans recommencer.
    [Fact]
    public async Task DatabaseFailureDuringTheSaveIsNotHiddenAndTheNextCodeWorksAfterIt()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        services.SessionRepository.IsDown = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => services.FinishLoginAsync(waitingLoginToken, services.CalculateCurrentCode(activatedAccount.Secret)));
        Assert.Empty(services.SessionRepository.SavedSessions);

        services.SessionRepository.IsDown = false;
        services.WaitForNextCode();
        var result = await services.FinishLoginAsync(waitingLoginToken, services.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<TwoFactorLoginResult.LoggedIn>(result);
    }

    [Fact]
    public async Task JournalFailureOnAWrongCodeIsNotHidden()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        services.SecurityEventRepository.IsDown = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => services.FinishLoginAsync(waitingLoginToken, services.FindWrongCode(activatedAccount.Secret)));
    }

    [Fact]
    public async Task DatabaseFailureWhileReadingTheBackupCodesIsNotHidden()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        services.TwoFactorRepository.FailsWhenReadingBackupCodes = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => services.FinishLoginAsync(waitingLoginToken, activatedAccount.BackupCodes[0]));
    }

    // Une clé perdue ne se fait pas passer pour un mauvais code : l'erreur remonte, et elle sera vue dans le journal technique.
    [Fact]
    public async Task LostEncryptionKeyIsNotHiddenForAuthenticatorCodesAndLeavesTheBackupCodesWorking()
    {
        var activatedAccount = services.AddActivatedAccount();
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();
        services.SecretProtector = new AesGcmTotpSecretProtector(TwoFactorTestTools.CreateEncryptionKey());

        await Assert.ThrowsAnyAsync<CryptographicException>(() => services.FinishLoginAsync(waitingLoginToken, services.CalculateCurrentCode(activatedAccount.Secret)));
        Assert.Empty(services.SessionRepository.SavedSessions);

        var result = await services.FinishLoginAsync(waitingLoginToken, activatedAccount.BackupCodes[0]);
        Assert.IsType<TwoFactorLoginResult.LoggedIn>(result);
    }

    // --- Les cookies déjà présents

    [Fact]
    public async Task KnownDeviceKeepsItsTokenAndTheOldSessionOfTheBrowserIsClosed()
    {
        var activatedAccount = services.AddActivatedAccount();
        var deviceToken = services.TokenGenerator.GenerateToken();
        services.SessionRepository.SavedDevices.Add(new Device { DeviceToken = deviceToken.Hash, UserId = activatedAccount.UserAccount.Id });
        var oldSessionToken = services.TokenGenerator.GenerateToken();
        services.SessionRepository.SavedSessions.Add(new UserSession { Id = oldSessionToken.Hash, LastActivityAt = services.Clock.GetUtcNow().UtcDateTime, UserId = activatedAccount.UserAccount.Id });
        var waitingLoginToken = await services.LogInThenGetWaitingLoginTokenAsync();

        var result = await services.CreateTwoFactorLoginService().FinishAsync(new TwoFactorCodeAttempt
        {
            Code = services.CalculateCurrentCode(activatedAccount.Secret),
            IpAddress = IpAddress,
            WaitingLoginToken = waitingLoginToken,
            DeviceToken = deviceToken.Token,
            SessionToken = oldSessionToken.Token
        });

        Assert.Equal(deviceToken.Token, Assert.IsType<TwoFactorLoginResult.LoggedIn>(result).DeviceToken);
        Assert.Single(services.SessionRepository.SavedDevices);
        var remainingSession = Assert.Single(services.SessionRepository.SavedSessions);
        Assert.NotEqual(oldSessionToken.Hash, remainingSession.Id);
    }
}
