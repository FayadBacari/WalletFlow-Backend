using System.Net;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.Emails;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Emails;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la récupération et le déblocage avec les vrais outils : Argon2, jetons, file d'e-mails et base PostgreSQL locale.
// Seuls l'envoi des e-mails et l'horloge sont remplacés. Chaque test annule sa transaction à la fin : la base reste vide.
public class AccountRecoveryRealChainTests
{
    private const string NewPassword = "Nouveau-Mot-De-Passe-2";

    private static readonly IPAddress IpAddress = IPAddress.Parse("203.0.113.7");

    private readonly LibsodiumPasswordHasher passwordHasher = RealArgonHashers.CreatePasswordHasher();
    private readonly RandomSecureTokenGenerator tokenGenerator = new RandomSecureTokenGenerator();
    private readonly FakeEmailSender emailSender = new FakeEmailSender();
    private readonly FakeTimeProvider clock = new FakeTimeProvider();

    // Les comptes et les adresses de ce test : seules leurs demandes d'e-mail sont traitées, jamais celles d'un autre test.
    private readonly List<Guid?> accountsOfThisTest = new List<Guid?>();
    private readonly List<string?> addressesOfThisTest = new List<string?>();

    // Enregistre un compte avec ce mot de passe, haché avec le vrai Argon2.
    private async Task<Guid> SaveAccountAsync(WalletFlowDatabaseContext databaseContext, string emailAddress, string password, bool isEmailConfirmed = true)
    {
        var newAccount = new User
        {
            Email = emailAddress,
            PasswordHash = await passwordHasher.HashAsync(password),
            IsEmailConfirmed = isEmailConfirmed
        };
        databaseContext.Users.Add(newAccount);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();
        accountsOfThisTest.Add(newAccount.Id);
        addressesOfThisTest.Add(emailAddress);

        return newAccount.Id;
    }

    // Fait une tentative de connexion comme le ferait une requête, avec son propre état en mémoire.
    private async Task<AccountLoginResult> LogInAsync(WalletFlowDatabaseContext databaseContext, string emailAddress, string password)
    {
        var loginService = new AccountLoginService(
            new UserAccountRepository(databaseContext),
            CreateSavingService(databaseContext),
            CreateSecurityJournal(databaseContext, clock),
            CreateEmailRequestRepository(databaseContext),
            passwordHasher,
            tokenGenerator,
            new InMemoryWaitingLoginStore(clock),
            new UserSessionOpeningService(
                new UserAccountRepository(databaseContext),
                new UserSessionRepository(databaseContext),
                new TwoFactorAuthenticationRepository(databaseContext),
                CreateEmailRequestRepository(databaseContext),
                CreateSavingService(databaseContext),
                CreateSecurityJournal(databaseContext, clock),
                tokenGenerator,
                clock),
            clock);

        var result = await loginService.LogInAsync(new LoginAttempt
        {
            EmailAddress = emailAddress,
            Password = password,
            IpAddress = IpAddress
        });
        databaseContext.ChangeTracker.Clear();

        return result;
    }

    // Le vrai accès à la file d'e-mails. Personne n'attend son réveil : ces tests traitent la file eux-mêmes.
    private static EmailRequestRepository CreateEmailRequestRepository(WalletFlowDatabaseContext databaseContext)
    {
        return new EmailRequestRepository(databaseContext, new EmailRequestWakeUp());
    }

    // Les demandes d'e-mail de ce test, dans l'ordre de la file.
    private async Task<List<EmailRequest>> FindRequestsOfThisTestAsync(WalletFlowDatabaseContext databaseContext)
    {
        return await databaseContext.EmailRequests.AsNoTracking()
            .Where(request => accountsOfThisTest.Contains(request.UserId) || addressesOfThisTest.Contains(request.EmailAddress))
            .OrderBy(request => request.NextTryAt)
            .ToListAsync();
    }

    // Fait un essai pour chaque demande de ce test, comme le ferait la tâche de fond.
    private async Task ProcessQueuedRequestsAsync(WalletFlowDatabaseContext databaseContext)
    {
        foreach (var request in await FindRequestsOfThisTestAsync(databaseContext))
        {
            var processingService = new EmailRequestProcessingService(
                CreateEmailRequestRepository(databaseContext),
                new UserAccountRepository(databaseContext),
                new AlertRepository(databaseContext),
                new SupportRequestRepository(databaseContext),
                new EntityFrameworkDatabaseUnitOfWork(databaseContext),
                CreateSecurityJournal(databaseContext, clock),
                tokenGenerator,
                emailSender,
                new FrenchAccountRecoveryEmails(),
                new FrenchAccountChangeEmails(),
                new FrenchSecurityAlertEmails(),
                new FrenchAlertEmails(),
                new FrenchSupportEmails(),
                new PublicWebsiteSettings("https://walletflow.test"),
                clock);

            await processingService.ProcessAsync(request.Id);
            databaseContext.ChangeTracker.Clear();
        }
    }

    // Demande un lien de récupération comme le ferait le visiteur : la demande entre dans la vraie file.
    private async Task RequestRecoveryAsync(WalletFlowDatabaseContext databaseContext, string emailAddress)
    {
        var recoveryRequestService = new PasswordRecoveryRequestService(
            new InMemoryPasswordRecoveryRequestLimiter(clock),
            CreateEmailRequestRepository(databaseContext),
            CreateSavingService(databaseContext),
            clock);
        addressesOfThisTest.Add(emailAddress);

        Assert.IsType<PasswordRecoveryRequestResult.RequestAccepted>(await recoveryRequestService.RequestAsync(emailAddress, IpAddress));
        databaseContext.ChangeTracker.Clear();
    }

    // Demande un lien de récupération et renvoie le jeton lu dans l'e-mail, comme le ferait le visiteur.
    // Le compte a pu recevoir d'autres e-mails en même temps, comme le lien de déblocage : seul celui de la récupération compte.
    private async Task<string> RequestRecoveryThenReadTokenAsync(WalletFlowDatabaseContext databaseContext, string emailAddress)
    {
        await RequestRecoveryAsync(databaseContext, emailAddress);
        await ProcessQueuedRequestsAsync(databaseContext);

        return ReadConfirmationToken(emailSender.SentEmails.Last(email => email.Body.Contains("/reset-password#token=")).Body);
    }

    // Envoie un nouveau mot de passe comme le ferait une requête.
    private async Task<PasswordResetResult> ResetPasswordAsync(WalletFlowDatabaseContext databaseContext, string linkToken, string newPassword)
    {
        var passwordResetService = new PasswordResetService(
            new UserAccountRepository(databaseContext),
            new UserSessionRepository(databaseContext),
            CreateEmailRequestRepository(databaseContext),
            CreateSavingService(databaseContext),
            CreateSecurityJournal(databaseContext, clock),
            passwordHasher,
            tokenGenerator,
            clock);

        var result = await passwordResetService.ResetAsync(linkToken, newPassword, IpAddress);
        databaseContext.ChangeTracker.Clear();

        return result;
    }

    // Clique sur le lien de déblocage comme le ferait une requête.
    private async Task<AccountUnlockResult> UnlockAccountAsync(WalletFlowDatabaseContext databaseContext, string linkToken)
    {
        var unlockService = new AccountUnlockService(new UserAccountRepository(databaseContext), CreateSavingService(databaseContext), CreateSecurityJournal(databaseContext, clock), tokenGenerator, clock);

        var result = await unlockService.UnlockAsync(linkToken, IpAddress);
        databaseContext.ChangeTracker.Clear();

        return result;
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

    // Tout le parcours : session ouverte, lien reçu, nouveau mot de passe, session fermée, seul le nouveau mot de passe marche.
    [Fact]
    public async Task RecoveryChangesThePasswordClosesTheSessionsAndOnlyTheNewPasswordWorks()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var accountId = await SaveAccountAsync(databaseContext, "chaine-recuperation@exemple.test", ValidPassword);
        var initialLoginResult = await LogInAsync(databaseContext, "chaine-recuperation@exemple.test", ValidPassword);
        Assert.IsType<AccountLoginResult.LoggedIn>(initialLoginResult);

        var linkToken = await RequestRecoveryThenReadTokenAsync(databaseContext, "chaine-recuperation@exemple.test");
        var resetResult = await ResetPasswordAsync(databaseContext, linkToken, NewPassword);

        Assert.IsType<PasswordResetResult.PasswordChanged>(resetResult);
        Assert.Equal(0, await databaseContext.UserSessions.CountAsync(row => row.UserId == accountId));
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, accountId, SecurityEventType.PasswordRecovery, SecurityEventResult.Success));
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, accountId, SecurityEventType.PasswordChange, SecurityEventResult.Success));

        var oldPasswordResult = await LogInAsync(databaseContext, "chaine-recuperation@exemple.test", ValidPassword);
        var newPasswordResult = await LogInAsync(databaseContext, "chaine-recuperation@exemple.test", NewPassword);
        Assert.IsNotType<AccountLoginResult.LoggedIn>(oldPasswordResult);
        Assert.IsType<AccountLoginResult.LoggedIn>(newPasswordResult);

        var reusedLinkResult = await ResetPasswordAsync(databaseContext, linkToken, "Encore-Un-Mot-De-Passe-3");
        Assert.IsType<PasswordResetResult.LinkNoLongerValid>(reusedLinkResult);
    }

    // Le vrai Argon2 voit deux mots différents sans la normalisation : c'est le seul test qui le prouve de bout en bout.
    [Fact]
    public async Task NewPasswordTypedWithComposedAccentLogsInWithDecomposedAccent()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        await SaveAccountAsync(databaseContext, "chaine-recuperation-accent@exemple.test", ValidPassword);
        var linkToken = await RequestRecoveryThenReadTokenAsync(databaseContext, "chaine-recuperation-accent@exemple.test");

        await ResetPasswordAsync(databaseContext, linkToken, "Mot-De-Passé-1");
        var result = await LogInAsync(databaseContext, "chaine-recuperation-accent@exemple.test", "Mot-De-Passe\u0301-1");

        Assert.IsType<AccountLoginResult.LoggedIn>(result);
    }

    // GR Auth : une récupération réussie débloque aussi le compte.
    [Fact]
    public async Task RecoveryUnlocksTheAccountOnTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var accountId = await SaveAccountAsync(databaseContext, "chaine-recuperation-blocage@exemple.test", ValidPassword);

        for (var attemptNumber = 1; attemptNumber <= 5; attemptNumber++)
        {
            await LogInAsync(databaseContext, "chaine-recuperation-blocage@exemple.test", "Mauvais-Mot-De-Passe-2");
        }

        var linkToken = await RequestRecoveryThenReadTokenAsync(databaseContext, "chaine-recuperation-blocage@exemple.test");
        await ResetPasswordAsync(databaseContext, linkToken, NewPassword);

        var reloadedAccount = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Id == accountId);
        Assert.Equal(0, reloadedAccount.FailedLoginAttemptCount);
        Assert.Null(reloadedAccount.LockoutEndsAt);
        Assert.Equal(1, reloadedAccount.LockoutCount);

        var result = await LogInAsync(databaseContext, "chaine-recuperation-blocage@exemple.test", NewPassword);
        Assert.IsType<AccountLoginResult.LoggedIn>(result);
    }

    // Tout le parcours du déblocage : 5 essais, un seul e-mail, lien qui débloque sans connecter.
    [Fact]
    public async Task UnlockLinkReceivedAfterTheFifthAttemptOpensTheAccountWithoutLoggingIn()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var accountId = await SaveAccountAsync(databaseContext, "chaine-deblocage@exemple.test", ValidPassword);

        for (var attemptNumber = 1; attemptNumber <= 7; attemptNumber++)
        {
            await LogInAsync(databaseContext, "chaine-deblocage@exemple.test", "Mauvais-Mot-De-Passe-2");
        }

        await ProcessQueuedRequestsAsync(databaseContext);

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Contains("https://walletflow.test/unlock-account#token=", email.Body);
        var linkToken = ReadConfirmationToken(email.Body);

        var unlockResult = await UnlockAccountAsync(databaseContext, linkToken);

        Assert.IsType<AccountUnlockResult.AccountUnlocked>(unlockResult);
        Assert.Equal(0, await databaseContext.UserSessions.CountAsync(row => row.UserId == accountId));
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, accountId, SecurityEventType.Unlock, SecurityEventResult.Success));

        var reloadedAccount = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Id == accountId);
        Assert.Equal(0, reloadedAccount.FailedLoginAttemptCount);
        Assert.Null(reloadedAccount.LockoutEndsAt);
        Assert.Equal(1, reloadedAccount.LockoutCount);

        var loginResult = await LogInAsync(databaseContext, "chaine-deblocage@exemple.test", ValidPassword);
        Assert.IsType<AccountLoginResult.LoggedIn>(loginResult);

        var reusedLinkResult = await UnlockAccountAsync(databaseContext, linkToken);
        Assert.IsType<AccountUnlockResult.LinkNoLongerValid>(reusedLinkResult);
    }

    [Fact]
    public async Task SecondRecoveryLinkReplacesTheFirstOneOnTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        await SaveAccountAsync(databaseContext, "chaine-deux-liens@exemple.test", ValidPassword);

        var firstRecoveryToken = await RequestRecoveryThenReadTokenAsync(databaseContext, "chaine-deux-liens@exemple.test");
        var secondRecoveryToken = await RequestRecoveryThenReadTokenAsync(databaseContext, "chaine-deux-liens@exemple.test");

        var firstResult = await ResetPasswordAsync(databaseContext, firstRecoveryToken, NewPassword);
        var secondResult = await ResetPasswordAsync(databaseContext, secondRecoveryToken, NewPassword);

        Assert.IsType<PasswordResetResult.LinkNoLongerValid>(firstResult);
        Assert.IsType<PasswordResetResult.PasswordChanged>(secondResult);
    }

    // Un lien de récupération arrivé à 30 minutes est refusé sur la vraie base, à la seconde près.
    [Fact]
    public async Task RecoveryLinkExpiresAfterThirtyMinutesOnTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        await SaveAccountAsync(databaseContext, "chaine-lien-expire@exemple.test", ValidPassword);
        var linkToken = await RequestRecoveryThenReadTokenAsync(databaseContext, "chaine-lien-expire@exemple.test");

        clock.Advance(TimeSpan.FromMinutes(30));
        var result = await ResetPasswordAsync(databaseContext, linkToken, NewPassword);

        Assert.IsType<PasswordResetResult.LinkNoLongerValid>(result);
    }

    [Fact]
    public async Task UnconfirmedAddressGetsNoLinkOnTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var accountId = await SaveAccountAsync(databaseContext, "chaine-non-confirmee@exemple.test", ValidPassword, isEmailConfirmed: false);

        await RequestRecoveryAsync(databaseContext, "chaine-non-confirmee@exemple.test");
        await ProcessQueuedRequestsAsync(databaseContext);

        Assert.DoesNotContain("#token=", Assert.Single(emailSender.SentEmails).Body);
        Assert.Equal(0, await databaseContext.EmailTokens.CountAsync(row => row.UserId == accountId));
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, accountId, SecurityEventType.PasswordRecovery, SecurityEventResult.Failure));
    }

    // Une adresse inconnue laisse une trace sans identifiant, avec l'adresse IP, et l'adresse tapée n'est écrite nulle part (SS §7).
    [Fact]
    public async Task UnknownAddressIsWrittenInTheRealJournalWithoutIdentifier()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var anonymousEventCountBefore = await databaseContext.SecurityEvents.CountAsync(row =>
            row.UserId == null && row.Type == SecurityEventType.PasswordRecovery);

        await RequestRecoveryAsync(databaseContext, "adresse-sans-compte@exemple.test");
        await ProcessQueuedRequestsAsync(databaseContext);

        Assert.Empty(emailSender.SentEmails);
        Assert.Equal(anonymousEventCountBefore + 1, await databaseContext.SecurityEvents.CountAsync(row =>
            row.UserId == null && row.Type == SecurityEventType.PasswordRecovery));
        Assert.Empty(await FindRequestsOfThisTestAsync(databaseContext));
    }

    // La demande attend dans la vraie file, sans compte ni secret, jusqu'à ce que son e-mail parte (SS §9, DD §9.4).
    [Fact]
    public async Task RecoveryRequestWaitsInTheRealQueueUntilItsEmailIsSent()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        await SaveAccountAsync(databaseContext, "chaine-file@exemple.test", ValidPassword);

        await RequestRecoveryAsync(databaseContext, "chaine-file@exemple.test");

        var queuedRequest = Assert.Single(await FindRequestsOfThisTestAsync(databaseContext));
        Assert.Equal(EmailRequestType.PasswordRecovery, queuedRequest.Type);
        Assert.Equal("chaine-file@exemple.test", queuedRequest.EmailAddress);
        Assert.Equal(IpAddress, queuedRequest.IpAddress);
        Assert.Null(queuedRequest.UserId);
        Assert.Equal(0, queuedRequest.FailedTryCount);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, queuedRequest.RequestedAt);
        Assert.Equal(0, await databaseContext.EmailTokens.CountAsync(row => accountsOfThisTest.Contains(row.UserId)));

        await ProcessQueuedRequestsAsync(databaseContext);

        Assert.Single(emailSender.SentEmails);
        Assert.Empty(await FindRequestsOfThisTestAsync(databaseContext));
    }

    // Un envoi raté laisse la demande dans la vraie file, avec l'heure de son prochain essai (SS §9).
    [Fact]
    public async Task FailedTryIsSavedInTheRealQueue()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        await SaveAccountAsync(databaseContext, "chaine-file-panne@exemple.test", ValidPassword);
        await RequestRecoveryAsync(databaseContext, "chaine-file-panne@exemple.test");
        emailSender.AlwaysFails = true;

        await ProcessQueuedRequestsAsync(databaseContext);

        var queuedRequest = Assert.Single(await FindRequestsOfThisTestAsync(databaseContext));
        Assert.Equal(1, queuedRequest.FailedTryCount);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddMinutes(1), queuedRequest.NextTryAt);
    }

    // Le nouveau mot de passe et son alerte partent ensemble : l'alerte attend dans la vraie file (GR Notif).
    [Fact]
    public async Task ResetQueuesThePasswordChangeAlertOnTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var accountId = await SaveAccountAsync(databaseContext, "chaine-alerte-mdp@exemple.test", ValidPassword);
        var linkToken = await RequestRecoveryThenReadTokenAsync(databaseContext, "chaine-alerte-mdp@exemple.test");

        await ResetPasswordAsync(databaseContext, linkToken, NewPassword);

        var alert = Assert.Single(await FindRequestsOfThisTestAsync(databaseContext));
        Assert.Equal(EmailRequestType.PasswordChange, alert.Type);
        Assert.Equal(accountId, alert.UserId);
        Assert.Null(alert.EmailAddress);
        Assert.Equal(IpAddress, alert.IpAddress);
    }
}
