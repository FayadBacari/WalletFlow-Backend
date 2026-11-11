using System.Net;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Emails;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la double authentification avec les vrais outils : Argon2, Otp.NET, AES-GCM, jetons et base PostgreSQL locale.
// Seuls l'envoi des e-mails et l'horloge sont remplacés. Chaque test annule sa transaction à la fin : la base reste vide.
public class TwoFactorRealChainTests
{
    private const string AccountEmail = "chaine-double-auth@exemple.test";

    private static readonly IPAddress IpAddress = IPAddress.Parse("203.0.113.7");

    private readonly LibsodiumPasswordHasher passwordHasher = RealArgonHashers.CreatePasswordHasher();
    private readonly LibsodiumBackupCodeHasher backupCodeHasher = RealArgonHashers.CreateBackupCodeHasher();
    private readonly RandomSecureTokenGenerator tokenGenerator = new RandomSecureTokenGenerator();
    private readonly OtpNetAuthenticatorCodeTool authenticatorCodeTool = new OtpNetAuthenticatorCodeTool();
    private readonly AesGcmTotpSecretProtector secretProtector = new AesGcmTotpSecretProtector(TwoFactorTestTools.CreateEncryptionKey());
    private readonly FakeEmailSender emailSender = new FakeEmailSender();
    private readonly FakeBackgroundEmailRequestQueue emailQueue = new FakeBackgroundEmailRequestQueue();
    private readonly FakeTimeProvider clock = new FakeTimeProvider();
    private readonly InMemoryWaitingLoginStore waitingLoginStore;
    private readonly InMemoryWaitingSecretStore waitingSecretStore;
    private readonly InMemoryUsedAuthenticatorCodesRegister usedCodesRegister;

    // Donne aux outils en mémoire la fausse horloge. Ils survivent aux requêtes, comme dans la vraie API.
    public TwoFactorRealChainTests()
    {
        waitingLoginStore = new InMemoryWaitingLoginStore(clock);
        waitingSecretStore = new InMemoryWaitingSecretStore(clock);
        usedCodesRegister = new InMemoryUsedAuthenticatorCodesRegister(clock);
    }

    // Inscrit puis confirme un compte, comme le feraient deux requêtes, et renvoie son identifiant.
    private async Task<Guid> RegisterThenConfirmAsync(WalletFlowDatabaseContext databaseContext)
    {
        var registrationService = new AccountRegistrationService(
            new UserAccountRepository(databaseContext),
            new EntityFrameworkDatabaseUnitOfWork(databaseContext),
            passwordHasher,
            tokenGenerator,
            emailSender,
            new FrenchAccountRegistrationEmails(),
            new PublicWebsiteSettings("https://walletflow.test"),
            new InMemoryEmailAddressRequestLimiter(clock),
            clock);
        await registrationService.RegisterAsync(AccountEmail, ValidPassword);
        databaseContext.ChangeTracker.Clear();

        var confirmationService = new AccountConfirmationService(
            new UserAccountRepository(databaseContext),
            new EntityFrameworkDatabaseUnitOfWork(databaseContext),
            passwordHasher,
            tokenGenerator,
            clock);
        var result = await confirmationService.ConfirmAsync(
            ReadConfirmationToken(emailSender.SentEmails.Last().Body),
            ValidPassword);
        Assert.Equal(AccountConfirmationResult.AccountConfirmed, result);
        databaseContext.ChangeTracker.Clear();

        var reloadedAccount = await databaseContext.Users.AsNoTracking().SingleAsync(row =>
            row.Email == AccountEmail);

        return reloadedAccount.Id;
    }

    // Crée le service qui vérifie les codes, sur cet accès à la base.
    private TwoFactorCodeCheckingService CreateCodeCheckingService(WalletFlowDatabaseContext databaseContext)
    {
        return new TwoFactorCodeCheckingService(
            new TwoFactorAuthenticationRepository(databaseContext),
            backupCodeHasher,
            authenticatorCodeTool,
            secretProtector,
            usedCodesRegister,
            clock);
    }

    // Crée le service qui ouvre les sessions, sur cet accès à la base.
    private UserSessionOpeningService CreateSessionOpeningService(WalletFlowDatabaseContext databaseContext)
    {
        return new UserSessionOpeningService(
            new UserAccountRepository(databaseContext),
            new UserSessionRepository(databaseContext),
            new TwoFactorAuthenticationRepository(databaseContext),
            CreateSavingService(databaseContext),
            CreateSecurityJournal(databaseContext, clock),
            tokenGenerator,
            clock);
    }

    // Active la double authentification comme le feraient deux requêtes. Renvoie le secret du téléphone et les codes de secours.
    private async Task<(string SecretOfThePhone, List<string> BackupCodesOfTheUser)> ActivateTwoFactorAsync(WalletFlowDatabaseContext databaseContext, Guid accountId)
    {
        var startResult = await CreateActivationService(databaseContext).StartAsync(accountId, ValidPassword, IpAddress);
        databaseContext.ChangeTracker.Clear();
        var secret = Assert.IsType<TwoFactorActivationStartResult.Started>(startResult).Secret;

        var finishResult = await CreateActivationService(databaseContext).FinishAsync(accountId, CalculateCurrentCode(secret), IpAddress);
        databaseContext.ChangeTracker.Clear();
        Assert.IsType<TwoFactorActivationFinishResult.Activated>(finishResult);

        return (secret, Assert.IsType<TwoFactorActivationFinishResult.Activated>(finishResult).BackupCodes);
    }

    // Crée le service qui active la double authentification, sur cet accès à la base.
    private TwoFactorActivationService CreateActivationService(WalletFlowDatabaseContext databaseContext)
    {
        return new TwoFactorActivationService(
            new UserAccountRepository(databaseContext),
            new TwoFactorAuthenticationRepository(databaseContext),
            CreateSavingService(databaseContext),
            CreateSecurityJournal(databaseContext, clock),
            passwordHasher,
            backupCodeHasher,
            tokenGenerator,
            authenticatorCodeTool,
            secretProtector,
            waitingSecretStore,
            CreateCodeCheckingService(databaseContext));
    }

    // Désactive comme le ferait une requête.
    private async Task<TwoFactorDeactivationResult> DeactivateAsync(WalletFlowDatabaseContext databaseContext, Guid accountId, string code)
    {
        var deactivationService = new TwoFactorDeactivationService(
            new UserAccountRepository(databaseContext),
            new TwoFactorAuthenticationRepository(databaseContext),
            CreateSavingService(databaseContext),
            CreateSecurityJournal(databaseContext, clock),
            passwordHasher,
            CreateCodeCheckingService(databaseContext));
        var result = await deactivationService.DeactivateAsync(accountId, ValidPassword, code, IpAddress);
        databaseContext.ChangeTracker.Clear();

        return result;
    }

    // Donne le mot de passe comme le ferait une requête.
    private async Task<AccountLoginResult> LogInWithPasswordAsync(WalletFlowDatabaseContext databaseContext, string password = ValidPassword)
    {
        var loginService = new AccountLoginService(
            new UserAccountRepository(databaseContext),
            CreateSavingService(databaseContext),
            CreateSecurityJournal(databaseContext, clock),
            emailQueue,
            passwordHasher,
            tokenGenerator,
            waitingLoginStore,
            CreateSessionOpeningService(databaseContext),
            clock);
        var result = await loginService.LogInAsync(new LoginAttempt
        {
            EmailAddress = AccountEmail,
            Password = password,
            IpAddress = IpAddress
        });
        databaseContext.ChangeTracker.Clear();

        return result;
    }

    // Finit la connexion comme le ferait une requête.
    private async Task<TwoFactorLoginResult> FinishLoginAsync(WalletFlowDatabaseContext databaseContext, string waitingLoginToken, string code)
    {
        var twoFactorLoginService = new TwoFactorLoginService(
            new UserAccountRepository(databaseContext),
            CreateSecurityJournal(databaseContext, clock),
            tokenGenerator,
            waitingLoginStore,
            CreateCodeCheckingService(databaseContext),
            CreateSessionOpeningService(databaseContext));
        var result = await twoFactorLoginService.FinishAsync(new TwoFactorCodeAttempt
        {
            Code = code,
            IpAddress = IpAddress,
            WaitingLoginToken = waitingLoginToken
        });
        databaseContext.ChangeTracker.Clear();

        return result;
    }

    // Calcule le code qu'affiche le téléphone à la date de la fausse horloge.
    private string CalculateCurrentCode(string secret)
    {
        return TwoFactorTestTools.CalculateCodeAt(secret, clock.GetUtcNow());
    }

    // Passe à la période suivante : le téléphone affiche un nouveau code.
    private void WaitForNextCode()
    {
        clock.Advance(TimeSpan.FromSeconds(TwoFactorTestTools.PeriodInSeconds));
    }

    // Compte les événements de ce type et de ce résultat dans le vrai journal, pour ce compte.
    private static async Task<int> CountSecurityEventsAsync(
        WalletFlowDatabaseContext databaseContext,
        Guid accountId,
        SecurityEventType type,
        SecurityEventResult result)
    {
        return await databaseContext.SecurityEvents.CountAsync(row =>
            row.UserId == accountId
            && row.Type == type
            && row.Result == result);
    }

    // Relit le compte dans la vraie base, sans passer par les objets en mémoire.
    private static async Task<User> ReadAccountAsync(WalletFlowDatabaseContext databaseContext, Guid accountId)
    {
        return await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Id == accountId);
    }

    // Tout le début du parcours : activer, puis se reconnecter avec un vrai code calculé par une vraie bibliothèque.
    [Fact]
    public async Task ActivationThenLoginWithARealCodeWorkOnTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var accountId = await RegisterThenConfirmAsync(databaseContext);

        var (secret, givenCodes) = await ActivateTwoFactorAsync(databaseContext, accountId);

        var reloadedAccount = await ReadAccountAsync(databaseContext, accountId);
        Assert.True(reloadedAccount.IsTwoFactorEnabled);
        Assert.DoesNotContain(secret, reloadedAccount.EncryptedTotpSecret);
        Assert.Equal(secret, secretProtector.Decrypt(reloadedAccount.EncryptedTotpSecret!, accountId));
        Assert.Equal(10, givenCodes.Count);
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, accountId, SecurityEventType.TwoFactorActivation, SecurityEventResult.Success));

        var storedCodes = await databaseContext.BackupCodes.AsNoTracking().Where(row => row.UserId == accountId).ToListAsync();
        Assert.Equal(10, storedCodes.Count);
        foreach (var storedCode in storedCodes)
        {
            Assert.StartsWith(LibsodiumBackupCodeHasher.HashPrefix, storedCode.CodeHash);
            Assert.Null(storedCode.UsedAt);
        }

        var passwordResult = await LogInWithPasswordAsync(databaseContext);
        Assert.IsType<AccountLoginResult.WaitingForTwoFactorCode>(passwordResult);
        Assert.Equal(0, await databaseContext.UserSessions.CountAsync(row => row.UserId == accountId));
        Assert.Equal(1, (await ReadAccountAsync(databaseContext, accountId)).FailedLoginAttemptCount);

        WaitForNextCode();
        var codeResult = await FinishLoginAsync(databaseContext, Assert.IsType<AccountLoginResult.WaitingForTwoFactorCode>(passwordResult).WaitingLoginToken, CalculateCurrentCode(secret));

        Assert.IsType<TwoFactorLoginResult.LoggedIn>(codeResult);
        Assert.Equal(1, await databaseContext.UserSessions.CountAsync(row => row.UserId == accountId));
        Assert.Equal(1, await databaseContext.Devices.CountAsync(row => row.UserId == accountId));
        Assert.Equal(0, (await ReadAccountAsync(databaseContext, accountId)).FailedLoginAttemptCount);
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, accountId, SecurityEventType.Login, SecurityEventResult.Success));

        var sessionService = new UserSessionService(new UserSessionRepository(databaseContext), tokenGenerator, clock);
        Assert.Equal(accountId, await sessionService.FindOwnerAndRecordActivityAsync(Assert.IsType<TwoFactorLoginResult.LoggedIn>(codeResult).SessionToken));
    }

    [Fact]
    public async Task WrongCodesAreWrittenInTheRealJournalAndOpenNothing()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var accountId = await RegisterThenConfirmAsync(databaseContext);
        var (secret, _) = await ActivateTwoFactorAsync(databaseContext, accountId);
        var passwordResult = await LogInWithPasswordAsync(databaseContext);
        var wrongCode = TwoFactorTestTools.FindWrongCodeAt(secret, clock.GetUtcNow());

        for (var attemptNumber = 1; attemptNumber <= 4; attemptNumber++)
        {
            var wrongCodeResult = await FinishLoginAsync(databaseContext, Assert.IsType<AccountLoginResult.WaitingForTwoFactorCode>(passwordResult).WaitingLoginToken, wrongCode);
            Assert.IsType<TwoFactorLoginResult.WrongCode>(wrongCodeResult);
        }

        var fifthResult = await FinishLoginAsync(databaseContext, Assert.IsType<AccountLoginResult.WaitingForTwoFactorCode>(passwordResult).WaitingLoginToken, wrongCode);

        Assert.IsType<TwoFactorLoginResult.LoginMustRestart>(fifthResult);
        Assert.Equal(5, await CountSecurityEventsAsync(databaseContext, accountId, SecurityEventType.Login, SecurityEventResult.Failure));
        Assert.Equal(0, await databaseContext.UserSessions.CountAsync(row => row.UserId == accountId));
    }

    // Les vrais codes passent par le vrai Argon2 : un code de secours n'est utilisable qu'une fois, écrit avec ou sans son tiret.
    [Fact]
    public async Task BackupCodeWorksOnceWithTheRealHasherOnTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var accountId = await RegisterThenConfirmAsync(databaseContext);
        var (_, givenCodes) = await ActivateTwoFactorAsync(databaseContext, accountId);

        var firstLogin = await LogInWithPasswordAsync(databaseContext);
        var firstUseResult = await FinishLoginAsync(databaseContext, Assert.IsType<AccountLoginResult.WaitingForTwoFactorCode>(firstLogin).WaitingLoginToken, givenCodes[7]);
        Assert.IsType<TwoFactorLoginResult.LoggedIn>(firstUseResult);
        Assert.Equal(1, await databaseContext.BackupCodes.CountAsync(row => row.UserId == accountId && row.UsedAt == clock.GetUtcNow().UtcDateTime));

        var secondLogin = await LogInWithPasswordAsync(databaseContext);
        var secondUseResult = await FinishLoginAsync(databaseContext, Assert.IsType<AccountLoginResult.WaitingForTwoFactorCode>(secondLogin).WaitingLoginToken, givenCodes[7].Replace("-", "").ToLowerInvariant());
        Assert.IsType<TwoFactorLoginResult.WrongCode>(secondUseResult);

        var otherCodeResult = await FinishLoginAsync(databaseContext, Assert.IsType<AccountLoginResult.WaitingForTwoFactorCode>(secondLogin).WaitingLoginToken, givenCodes[2].Replace("-", "").ToLowerInvariant());
        Assert.IsType<TwoFactorLoginResult.LoggedIn>(otherCodeResult);
    }

    // Le verrou réel : 5 connexions avec le bon mot de passe, puis le propriétaire entre avec son code et le blocage disparaît.
    [Fact]
    public async Task LoginsWithTheRightPasswordOnlyReallyLockTheAccountAndTheRightCodeRemovesTheLock()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var accountId = await RegisterThenConfirmAsync(databaseContext);
        var (secret, _) = await ActivateTwoFactorAsync(databaseContext, accountId);
        WaitForNextCode();

        var lastWaitingLoginToken = "";
        for (var attemptNumber = 1; attemptNumber <= 5; attemptNumber++)
        {
            var attemptResult = await LogInWithPasswordAsync(databaseContext);
            lastWaitingLoginToken = Assert.IsType<AccountLoginResult.WaitingForTwoFactorCode>(attemptResult).WaitingLoginToken;
        }

        var lockedAccount = await ReadAccountAsync(databaseContext, accountId);
        Assert.NotNull(lockedAccount.LockoutEndsAt);
        Assert.Equal(1, lockedAccount.LockoutCount);
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, accountId, SecurityEventType.Lockout, SecurityEventResult.Success));
        Assert.Single(emailQueue.AddedRequests);

        var lockedResult = await LogInWithPasswordAsync(databaseContext);
        Assert.IsNotType<AccountLoginResult.WaitingForTwoFactorCode>(lockedResult);

        var codeResult = await FinishLoginAsync(databaseContext, lastWaitingLoginToken, CalculateCurrentCode(secret));

        Assert.IsType<TwoFactorLoginResult.LoggedIn>(codeResult);
        var loggedInAccount = await ReadAccountAsync(databaseContext, accountId);
        Assert.Null(loggedInAccount.LockoutEndsAt);
        Assert.Equal(0, loggedInAccount.FailedLoginAttemptCount);
        Assert.Equal(0, loggedInAccount.LockoutCount);
    }

    // La fin du cycle : désactiver efface vraiment tout, l'événement passe la contrainte de la vraie base, et réactiver donne 10 nouveaux codes.
    [Fact]
    public async Task DeactivationThenReactivationWorkOnTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var accountId = await RegisterThenConfirmAsync(databaseContext);
        var (firstPhoneSecret, firstBackupCodes) = await ActivateTwoFactorAsync(databaseContext, accountId);
        var firstCodeHash = (await databaseContext.BackupCodes.AsNoTracking().FirstAsync(row => row.UserId == accountId)).CodeHash;

        var deactivationResult = await DeactivateAsync(databaseContext, accountId, firstBackupCodes[0]);

        Assert.Equal(TwoFactorDeactivationResult.TwoFactorDeactivated, deactivationResult);
        var reloadedAccount = await ReadAccountAsync(databaseContext, accountId);
        Assert.False(reloadedAccount.IsTwoFactorEnabled);
        Assert.Null(reloadedAccount.EncryptedTotpSecret);
        Assert.Equal(0, await databaseContext.BackupCodes.CountAsync(row => row.UserId == accountId));
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, accountId, SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Success));

        var loginResult = await LogInWithPasswordAsync(databaseContext);
        Assert.IsType<AccountLoginResult.LoggedIn>(loginResult);

        WaitForNextCode();
        var (secondPhoneSecret, _) = await ActivateTwoFactorAsync(databaseContext, accountId);

        Assert.NotEqual(firstPhoneSecret, secondPhoneSecret);
        Assert.Equal(10, await databaseContext.BackupCodes.CountAsync(row => row.UserId == accountId));
        Assert.False(await databaseContext.BackupCodes.AnyAsync(row => row.CodeHash == firstCodeHash));
    }

    // Un mot de passe faux sur une désactivation laisse tout en place et s'écrit au vrai journal.
    [Fact]
    public async Task RefusedActivationAndDeactivationAreWrittenInTheRealJournal()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var accountId = await RegisterThenConfirmAsync(databaseContext);

        await CreateActivationService(databaseContext).StartAsync(accountId, "Mauvais-Mot-De-Passe-2", IpAddress);
        databaseContext.ChangeTracker.Clear();
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, accountId, SecurityEventType.TwoFactorActivation, SecurityEventResult.Failure));

        await ActivateTwoFactorAsync(databaseContext, accountId);

        var result = await DeactivateAsync(databaseContext, accountId, "000000");

        Assert.NotEqual(TwoFactorDeactivationResult.TwoFactorDeactivated, result);
        Assert.True((await ReadAccountAsync(databaseContext, accountId)).IsTwoFactorEnabled);
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, accountId, SecurityEventType.TwoFactorDeactivation, SecurityEventResult.Failure));
    }

    // GR Auth : la récupération garde la double authentification, annule les connexions en attente, et la suivante demande le code.
    [Fact]
    public async Task PasswordRecoveryKeepsTheTwoFactorOnTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var accountId = await RegisterThenConfirmAsync(databaseContext);
        var (secret, _) = await ActivateTwoFactorAsync(databaseContext, accountId);
        WaitForNextCode();
        var thiefLogin = await LogInWithPasswordAsync(databaseContext);

        var recoveryToken = tokenGenerator.GenerateToken();
        await new UserAccountRepository(databaseContext).ReplaceTokenAsync(new EmailToken
        {
            Type = EmailTokenType.PasswordRecovery,
            TokenHash = recoveryToken.Hash,
            ExpiresAt = clock.GetUtcNow().UtcDateTime.AddMinutes(30),
            UserId = accountId
        });
        databaseContext.ChangeTracker.Clear();
        var passwordResetService = new PasswordResetService(
            new UserAccountRepository(databaseContext),
            new UserSessionRepository(databaseContext),
            CreateSavingService(databaseContext),
            CreateSecurityJournal(databaseContext, clock),
            passwordHasher,
            tokenGenerator,
            clock);
        var recoveryResult = await passwordResetService.ResetAsync(recoveryToken.Token, "Nouveau-Mot-De-Passe-3", IpAddress);
        databaseContext.ChangeTracker.Clear();
        Assert.Equal(PasswordResetResult.PasswordChanged, recoveryResult);

        var thiefResult = await FinishLoginAsync(databaseContext, Assert.IsType<AccountLoginResult.WaitingForTwoFactorCode>(thiefLogin).WaitingLoginToken, CalculateCurrentCode(secret));
        Assert.IsType<TwoFactorLoginResult.LoginMustRestart>(thiefResult);

        var reloadedAccount = await ReadAccountAsync(databaseContext, accountId);
        Assert.True(reloadedAccount.IsTwoFactorEnabled);
        Assert.NotNull(reloadedAccount.EncryptedTotpSecret);

        var ownerLogin = await LogInWithPasswordAsync(databaseContext, "Nouveau-Mot-De-Passe-3");
        Assert.IsType<AccountLoginResult.WaitingForTwoFactorCode>(ownerLogin);
        var ownerResult = await FinishLoginAsync(databaseContext, Assert.IsType<AccountLoginResult.WaitingForTwoFactorCode>(ownerLogin).WaitingLoginToken, CalculateCurrentCode(secret));
        Assert.IsType<TwoFactorLoginResult.LoggedIn>(ownerResult);
    }

    // Le service de nettoyage ou d'effacement du compte passe par ON DELETE CASCADE : les codes de secours partent avec le compte.
    [Fact]
    public async Task BackupCodesDisappearWithTheAccountOnTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var accountId = await RegisterThenConfirmAsync(databaseContext);
        await ActivateTwoFactorAsync(databaseContext, accountId);

        await databaseContext.Users.Where(row => row.Id == accountId).ExecuteDeleteAsync();

        Assert.Equal(0, await databaseContext.BackupCodes.CountAsync(row => row.UserId == accountId));
    }
}
