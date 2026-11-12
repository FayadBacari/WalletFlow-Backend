using System.Net;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Configuration;
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

// Vérifie la connexion avec les vrais outils : Argon2, jetons et base PostgreSQL locale.
// Seuls l'envoi des e-mails et l'horloge sont remplacés. Chaque test annule sa transaction à la fin : la base reste vide.
public class AccountLoginRealChainTests
{
    private const string WrongPassword = "Mauvais-Mot-De-Passe-2";

    private static readonly IPAddress IpAddress = IPAddress.Parse("203.0.113.7");

    private readonly LibsodiumPasswordHasher passwordHasher = RealArgonHashers.CreatePasswordHasher();
    private readonly RandomSecureTokenGenerator tokenGenerator = new RandomSecureTokenGenerator();
    private readonly FakeEmailSender emailSender = new FakeEmailSender();
    private readonly FakeTimeProvider clock = new FakeTimeProvider();

    // Inscrit puis confirme un compte, comme le feraient deux requêtes.
    private async Task<Guid> RegisterThenConfirmAsync(WalletFlowDatabaseContext databaseContext, string emailAddress, string password)
    {
        var newUserId = await RegisterUnconfirmedAccountAsync(databaseContext, emailAddress, password);

        var confirmationService = new AccountConfirmationService(
            new UserAccountRepository(databaseContext),
            new EntityFrameworkDatabaseUnitOfWork(databaseContext),
            passwordHasher,
            tokenGenerator,
            clock);
        var result = await confirmationService.ConfirmAsync(
            ReadConfirmationToken(emailSender.SentEmails.Last().Body),
            password);
        Assert.Equal(AccountConfirmationResult.AccountConfirmed, result);
        databaseContext.ChangeTracker.Clear();

        return newUserId;
    }

    // Inscrit un compte sans le confirmer, comme le ferait une requête.
    private async Task<Guid> RegisterUnconfirmedAccountAsync(WalletFlowDatabaseContext databaseContext, string emailAddress, string password)
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
        var result = await registrationService.RegisterAsync(emailAddress, password);
        Assert.IsType<AccountRegistrationResult.Accepted>(result);
        databaseContext.ChangeTracker.Clear();

        var reloadedAccount = await databaseContext.Users.AsNoTracking().SingleAsync(row =>
            row.Email == emailAddress);

        return reloadedAccount.Id;
    }

    // Fait une tentative de connexion comme le ferait une requête, avec son propre état en mémoire.
    private async Task<AccountLoginResult> LogInAsync(WalletFlowDatabaseContext databaseContext, string emailAddress, string password)
    {
        var loginService = new AccountLoginService(
            new UserAccountRepository(databaseContext),
            CreateSavingService(databaseContext),
            CreateSecurityJournal(databaseContext, clock),
            new FakeBackgroundEmailRequestQueue(),
            passwordHasher,
            tokenGenerator,
            new InMemoryWaitingLoginStore(clock),
            new UserSessionOpeningService(
                new UserAccountRepository(databaseContext),
                new UserSessionRepository(databaseContext),
                new TwoFactorAuthenticationRepository(databaseContext),
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

    // Crée le service qui reconnaît les sessions, sur cet accès à la base.
    private UserSessionService CreateSessionService(WalletFlowDatabaseContext databaseContext)
    {
        return new UserSessionService(new UserSessionRepository(databaseContext), tokenGenerator, clock);
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

    // Tout le parcours : inscription, confirmation, connexion, session reconnue, déconnexion.
    [Fact]
    public async Task SignupConfirmationLoginAndLogoutWorkTogetherOnTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var newUserId = await RegisterThenConfirmAsync(databaseContext, "chaine-connexion@exemple.test", ValidPassword);

        var result = await LogInAsync(databaseContext, "chaine-connexion@exemple.test", ValidPassword);

        Assert.IsType<AccountLoginResult.LoggedIn>(result);
        var reloadedSession = await databaseContext.UserSessions.AsNoTracking().SingleAsync(row =>
            row.UserId == newUserId);
        Assert.NotEqual(Assert.IsType<AccountLoginResult.LoggedIn>(result).SessionToken, reloadedSession.Id);
        Assert.Equal(64, reloadedSession.Id.Length);
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, newUserId, SecurityEventType.Login, SecurityEventResult.Success));
        Assert.Equal(1, await databaseContext.Devices.CountAsync(row => row.UserId == newUserId));

        var sessionService = CreateSessionService(databaseContext);
        Assert.Equal(newUserId, await sessionService.FindOwnerAndRecordActivityAsync(Assert.IsType<AccountLoginResult.LoggedIn>(result).SessionToken));

        await sessionService.CloseAsync(Assert.IsType<AccountLoginResult.LoggedIn>(result).SessionToken);

        Assert.Null(await sessionService.FindOwnerAndRecordActivityAsync(Assert.IsType<AccountLoginResult.LoggedIn>(result).SessionToken));
    }

    // Le vrai Argon2 voit deux mots différents sans la normalisation : c'est le seul test qui le prouve de bout en bout.
    [Fact]
    public async Task PasswordTypedWithDecomposedAccentLogsInWithTheRealHasher()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        await RegisterThenConfirmAsync(databaseContext, "chaine-accent@exemple.test", "Mot-De-Passé-1");

        var result = await LogInAsync(databaseContext, "chaine-accent@exemple.test", "Mot-De-Passe\u0301-1");

        Assert.IsType<AccountLoginResult.LoggedIn>(result);
    }

    [Fact]
    public async Task FiveWrongPasswordsReallyLockTheAccountThenItOpensAgainAfterTheLockout()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var newUserId = await RegisterThenConfirmAsync(databaseContext, "chaine-blocage@exemple.test", ValidPassword);

        for (var attemptNumber = 1; attemptNumber <= 5; attemptNumber++)
        {
            var wrongPasswordResult = await LogInAsync(databaseContext, "chaine-blocage@exemple.test", WrongPassword);
            Assert.IsNotType<AccountLoginResult.LoggedIn>(wrongPasswordResult);
        }

        var lockedAccount = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Id == newUserId);
        Assert.Equal(5, lockedAccount.FailedLoginAttemptCount);
        Assert.Equal(1, lockedAccount.LockoutCount);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddMinutes(15), lockedAccount.LockoutEndsAt);

        var lockedResult = await LogInAsync(databaseContext, "chaine-blocage@exemple.test", ValidPassword);
        Assert.IsType<AccountLoginResult.WrongCredentials>(lockedResult);

        clock.Advance(TimeSpan.FromMinutes(15));
        var unlockedResult = await LogInAsync(databaseContext, "chaine-blocage@exemple.test", ValidPassword);
        Assert.IsType<AccountLoginResult.LoggedIn>(unlockedResult);

        var loggedInAccount = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Id == newUserId);
        Assert.Equal(0, loggedInAccount.FailedLoginAttemptCount);
        Assert.Equal(0, loggedInAccount.LockoutCount);
        Assert.Null(loggedInAccount.LockoutEndsAt);

        Assert.Equal(6, await CountSecurityEventsAsync(databaseContext, newUserId, SecurityEventType.Login, SecurityEventResult.Failure));
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, newUserId, SecurityEventType.Lockout, SecurityEventResult.Success));
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, newUserId, SecurityEventType.Login, SecurityEventResult.Success));
    }

    [Fact]
    public async Task SessionExpiresAfterThirtyMinutesOnTheRealDatabaseAndActivityExtendsIt()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var newUserId = await RegisterThenConfirmAsync(databaseContext, "chaine-expiration@exemple.test", ValidPassword);
        var result = await LogInAsync(databaseContext, "chaine-expiration@exemple.test", ValidPassword);
        var sessionService = CreateSessionService(databaseContext);

        clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(newUserId, await sessionService.FindOwnerAndRecordActivityAsync(Assert.IsType<AccountLoginResult.LoggedIn>(result).SessionToken));

        clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(newUserId, await sessionService.FindOwnerAndRecordActivityAsync(Assert.IsType<AccountLoginResult.LoggedIn>(result).SessionToken));

        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Null(await sessionService.FindOwnerAndRecordActivityAsync(Assert.IsType<AccountLoginResult.LoggedIn>(result).SessionToken));
    }

    [Fact]
    public async Task UnconfirmedAccountIsInvitedToConfirmOnTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var newUserId = await RegisterUnconfirmedAccountAsync(databaseContext, "chaine-non-confirme@exemple.test", ValidPassword);

        var result = await LogInAsync(databaseContext, "chaine-non-confirme@exemple.test", ValidPassword);

        Assert.IsType<AccountLoginResult.EmailAddressNotConfirmed>(result);
        Assert.Equal(0, await databaseContext.UserSessions.CountAsync(row => row.UserId == newUserId));
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, newUserId, SecurityEventType.Login, SecurityEventResult.Failure));
    }

    // Une adresse inconnue laisse une trace sans identifiant, et l'adresse tapée n'est écrite nulle part (SS §7).
    [Fact]
    public async Task UnknownEmailAddressIsWrittenInTheRealJournalWithoutIdentifier()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var anonymousEventCountBefore = await databaseContext.SecurityEvents.CountAsync(row => row.UserId == null);

        var result = await LogInAsync(databaseContext, "adresse-sans-compte@exemple.test", ValidPassword);

        Assert.IsType<AccountLoginResult.WrongCredentials>(result);
        Assert.Equal(anonymousEventCountBefore + 1, await databaseContext.SecurityEvents.CountAsync(row => row.UserId == null));
    }

    // PostgreSQL refuse le caractère nul : sans le contrôle du format, ce visiteur recevrait une erreur 500.
    [Fact]
    public async Task EmailAddressWithNullCharacterGetsTheNormalRefusalOnTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();

        var result = await LogInAsync(databaseContext, "nul\0caractere@exemple.test", ValidPassword);

        Assert.IsType<AccountLoginResult.WrongCredentials>(result);
    }
}
