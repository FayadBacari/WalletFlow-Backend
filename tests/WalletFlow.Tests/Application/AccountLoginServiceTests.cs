using System.Net;
using WalletFlow.Application.UserSessions;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le cas d'utilisation de la connexion (GR Auth, SS §2, §3 et §11), avec de faux outils.
// Seuls les jetons sont les vrais : il faut que chaque jeton soit différent pour comparer session et appareil.
public class AccountLoginServiceTests
{
    private const string AccountEmail = "utilisateur@exemple.test";
    private const string AccountPassword = "Mot-De-Passe-Valide-1";
    private const string WrongPassword = "Mauvais-Mot-De-Passe-2";

    private static readonly IPAddress IpAddress = IPAddress.Parse("203.0.113.7");

    private readonly FakeUserAccountRepository accountRepository = new FakeUserAccountRepository();
    private readonly FakeSecurityEventRepository securityEventRepository = new FakeSecurityEventRepository();
    private readonly FakeUserSessionRepository sessionRepository = new FakeUserSessionRepository();
    private readonly FakeTwoFactorAuthenticationRepository twoFactorRepository = new FakeTwoFactorAuthenticationRepository();
    private readonly FakeDatabaseUnitOfWork unitOfWork;
    private readonly InMemoryWaitingLoginStore waitingLoginStore;
    private readonly FakeBackgroundEmailRequestQueue emailQueue = new FakeBackgroundEmailRequestQueue();
    private readonly FakePasswordHasher passwordHasher = new FakePasswordHasher();
    private readonly RandomSecureTokenGenerator tokenGenerator = new RandomSecureTokenGenerator();
    private readonly FakeTimeProvider clock = new FakeTimeProvider();

    // Relie les faux accès à la fausse unité de travail, qui garde leurs changements ensemble.
    public AccountLoginServiceTests()
    {
        unitOfWork = new FakeDatabaseUnitOfWork(
            accountRepository,
            sessionRepository,
            twoFactorRepository,
            securityEventRepository);
        waitingLoginStore = new InMemoryWaitingLoginStore(clock);
    }

    // Crée le service à tester, avec ses faux outils.
    private AccountLoginService CreateLoginService()
    {
        var savingService = new DatabaseChangesSavingService(unitOfWork);
        var securityJournal = new SecurityJournalService(securityEventRepository, clock);

        return new AccountLoginService(
            accountRepository,
            savingService,
            securityJournal,
            emailQueue,
            passwordHasher,
            tokenGenerator,
            waitingLoginStore,
            new UserSessionOpeningService(
                accountRepository,
                sessionRepository,
                twoFactorRepository,
                savingService,
                securityJournal,
                tokenGenerator,
                clock),
            clock);
    }

    // Ajoute à la fausse base un compte avec l'adresse de ces tests.
    private User AddAccount(
        bool isEmailConfirmed = true,
        short failedAttemptCount = 0,
        short lockoutCount = 0)
    {
        var account = new User
        {
            Email = AccountEmail,
            PasswordHash = FakePasswordHasher.HashPrefix + AccountPassword,
            IsEmailConfirmed = isEmailConfirmed,
            FailedLoginAttemptCount = failedAttemptCount,
            LockoutCount = lockoutCount
        };
        accountRepository.SavedUsers.Add(account);

        return account;
    }

    // Fait une tentative de connexion, sans cookie déjà présent.
    private async Task<AccountLoginResult> LogInAsync(string emailAddress, string password)
    {
        return await CreateLoginService().LogInAsync(new LoginAttempt
        {
            EmailAddress = emailAddress,
            Password = password,
            IpAddress = IpAddress
        });
    }

    // Fait cette série de mauvais essais sur le compte de ces tests.
    private async Task TryWrongPasswordAsync(int wrongPasswordCount)
    {
        for (var attemptNumber = 1; attemptNumber <= wrongPasswordCount; attemptNumber++)
        {
            await LogInAsync(AccountEmail, WrongPassword);
        }
    }

    // Vérifie que la connexion est refusée avec le message générique, sans session.
    private void AssertGenericRefusal(AccountLoginResult result)
    {
        Assert.IsType<AccountLoginResult.WrongCredentials>(result);
        Assert.Empty(sessionRepository.SavedSessions);
    }

    // Compte les événements de ce type et de ce résultat dans le faux journal.
    private int CountSecurityEvents(SecurityEventType type, SecurityEventResult result)
    {
        var eventCount = 0;

        foreach (var securityEvent in securityEventRepository.SavedSecurityEvents)
        {
            if (securityEvent.Type == type && securityEvent.Result == result)
            {
                eventCount = eventCount + 1;
            }
        }

        return eventCount;
    }

    // --- Connexion réussie

    [Fact]
    public async Task RightPasswordOpensSessionAndStoresOnlyTheHashOfItsToken()
    {
        var account = AddAccount();

        var result = await LogInAsync(AccountEmail, AccountPassword);

        Assert.IsType<AccountLoginResult.LoggedIn>(result);
        var session = Assert.Single(sessionRepository.SavedSessions);
        Assert.Equal(account.Id, session.UserId);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, session.LastActivityAt);
        Assert.NotEqual(Assert.IsType<AccountLoginResult.LoggedIn>(result).SessionToken, session.Id);
        Assert.Equal(tokenGenerator.HashToken(Assert.IsType<AccountLoginResult.LoggedIn>(result).SessionToken), session.Id);
    }

    // SS §3 : un nouvel identifiant à chaque connexion, pour qu'un attaquant ne puisse pas imposer le sien.
    [Fact]
    public async Task EveryLoginGetsANewSessionToken()
    {
        AddAccount();

        var firstResult = await LogInAsync(AccountEmail, AccountPassword);
        var secondResult = await LogInAsync(AccountEmail, AccountPassword);

        Assert.NotEqual(Assert.IsType<AccountLoginResult.LoggedIn>(firstResult).SessionToken, Assert.IsType<AccountLoginResult.LoggedIn>(secondResult).SessionToken);
        Assert.Equal(2, sessionRepository.SavedSessions.Count);
    }

    // L'adresse tapée avec des majuscules et des espaces désigne le même compte.
    [Fact]
    public async Task EmailAddressIsSearchedInItsStoredForm()
    {
        AddAccount();

        var result = await LogInAsync(" Utilisateur@Exemple.TEST ", AccountPassword);

        Assert.IsType<AccountLoginResult.LoggedIn>(result);
    }

    [Fact]
    public async Task SuccessfulLoginResetsTheCountersOfTheAccount()
    {
        var account = AddAccount(failedAttemptCount: 3, lockoutCount: 2);

        await LogInAsync(AccountEmail, AccountPassword);

        Assert.Equal(0, account.FailedLoginAttemptCount);
        Assert.Equal(0, account.LockoutCount);
    }

    // Le 5e essai est compté avant la vérification : avec le bon mot de passe, le compte ne doit pas rester bloqué.
    [Fact]
    public async Task RightPasswordOnTheFifthAttemptLogsInAndLeavesNoLockout()
    {
        var account = AddAccount();
        await TryWrongPasswordAsync(4);

        var result = await LogInAsync(AccountEmail, AccountPassword);

        Assert.IsType<AccountLoginResult.LoggedIn>(result);
        Assert.Null(account.LockoutEndsAt);
        Assert.Equal(0, account.LockoutCount);
        Assert.Equal(0, CountSecurityEvents(SecurityEventType.Lockout, SecurityEventResult.Success));
    }

    [Fact]
    public async Task SuccessfulLoginIsWrittenInTheJournalWithItsDateUserAndIpAddress()
    {
        var account = AddAccount();

        await LogInAsync(AccountEmail, AccountPassword);

        var securityEvent = Assert.Single(securityEventRepository.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.Login, securityEvent.Type);
        Assert.Equal(SecurityEventResult.Success, securityEvent.Result);
        Assert.Equal(account.Id, securityEvent.UserId);
        Assert.Equal(IpAddress, securityEvent.IpAddress);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, securityEvent.OccurredAt);
    }

    // --- Mauvais identifiants

    [Fact]
    public async Task WrongPasswordGetsTheGenericMessageAndCountsOneAttempt()
    {
        var account = AddAccount();

        var result = await LogInAsync(AccountEmail, WrongPassword);

        AssertGenericRefusal(result);
        Assert.Equal(1, account.FailedLoginAttemptCount);
        Assert.Equal(1, accountRepository.LoginCounterSaveCount);

        var securityEvent = Assert.Single(securityEventRepository.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.Login, securityEvent.Type);
        Assert.Equal(SecurityEventResult.Failure, securityEvent.Result);
        Assert.Equal(account.Id, securityEvent.UserId);
        Assert.Equal(IpAddress, securityEvent.IpAddress);
    }

    // Sans ce calcul, une réponse plus rapide révélerait quelles adresses n'ont pas de compte (SS §2).
    [Fact]
    public async Task UnknownEmailAddressGetsTheSameMessageAndStillComputesOneHash()
    {
        var result = await LogInAsync("inconnu@exemple.test", AccountPassword);

        AssertGenericRefusal(result);
        Assert.Equal(1, passwordHasher.HashedPasswordCount);

        var securityEvent = Assert.Single(securityEventRepository.SavedSecurityEvents);
        Assert.Equal(SecurityEventResult.Failure, securityEvent.Result);
        Assert.Null(securityEvent.UserId);
        Assert.Equal(IpAddress, securityEvent.IpAddress);
    }

    // Une adresse mal formée ne doit jamais arriver à la base : le caractère nul y provoquerait une erreur.
    [Theory]
    [InlineData("sans-arobase")]
    [InlineData("nul\0caractere@exemple.test")]
    [InlineData("")]
    public async Task MalformedEmailAddressIsNeverSentToTheDatabase(string malformedEmailAddress)
    {
        var result = await LogInAsync(malformedEmailAddress, AccountPassword);

        AssertGenericRefusal(result);
        Assert.Equal(0, accountRepository.EmailSearchCount);
        Assert.Equal(1, passwordHasher.HashedPasswordCount);
    }

    // --- Compte non confirmé

    // Le message qui invite à confirmer n'apparaît qu'avec le bon mot de passe (GR Auth, SS §2).
    [Fact]
    public async Task UnconfirmedAccountWithRightPasswordIsInvitedToConfirmWithoutSession()
    {
        var account = AddAccount(isEmailConfirmed: false);

        var result = await LogInAsync(AccountEmail, AccountPassword);

        Assert.IsType<AccountLoginResult.EmailAddressNotConfirmed>(result);
        Assert.Empty(sessionRepository.SavedSessions);
        Assert.Equal(0, account.FailedLoginAttemptCount);
        Assert.Equal(1, CountSecurityEvents(SecurityEventType.Login, SecurityEventResult.Failure));
    }

    [Fact]
    public async Task UnconfirmedAccountWithWrongPasswordGetsTheGenericMessage()
    {
        var account = AddAccount(isEmailConfirmed: false);

        var result = await LogInAsync(AccountEmail, WrongPassword);

        AssertGenericRefusal(result);
        var securityEvent = Assert.Single(securityEventRepository.SavedSecurityEvents);
        Assert.Equal(SecurityEventResult.Failure, securityEvent.Result);
        Assert.Equal(account.Id, securityEvent.UserId);
    }

    // Un compte non confirmé n'est jamais bloqué : ses essais ne comptent pas (SS §2).
    [Fact]
    public async Task UnconfirmedAccountIsNeverLockedHoweverManyWrongPasswordsAreTried()
    {
        var account = AddAccount(isEmailConfirmed: false);

        await TryWrongPasswordAsync(6);

        Assert.Equal(0, account.FailedLoginAttemptCount);
        Assert.Equal(0, account.LockoutCount);
        Assert.Null(account.LockoutEndsAt);
        Assert.Equal(0, accountRepository.LoginCounterSaveCount);
        Assert.Equal(0, CountSecurityEvents(SecurityEventType.Lockout, SecurityEventResult.Success));
    }

    // --- Blocage

    [Fact]
    public async Task FifthWrongPasswordLocksTheAccountAndWritesOneLockoutEvent()
    {
        var account = AddAccount();

        await TryWrongPasswordAsync(5);

        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddMinutes(15), account.LockoutEndsAt);
        Assert.Equal(1, CountSecurityEvents(SecurityEventType.Lockout, SecurityEventResult.Success));
        Assert.Equal(5, CountSecurityEvents(SecurityEventType.Login, SecurityEventResult.Failure));
    }

    [Fact]
    public async Task FourWrongPasswordsDoNotWriteAnyLockoutEvent()
    {
        var account = AddAccount();

        await TryWrongPasswordAsync(4);

        Assert.Null(account.LockoutEndsAt);
        Assert.Equal(0, CountSecurityEvents(SecurityEventType.Lockout, SecurityEventResult.Success));
    }

    // Pendant le blocage, le message reste générique, même avec le bon mot de passe (SS §2).
    [Fact]
    public async Task RightPasswordDuringTheLockoutGetsTheGenericMessageWithoutChangingTheAccount()
    {
        var account = AddAccount();
        await TryWrongPasswordAsync(5);
        var lockoutEndBefore = account.LockoutEndsAt;

        var result = await LogInAsync(AccountEmail, AccountPassword);

        AssertGenericRefusal(result);
        Assert.Equal(lockoutEndBefore, account.LockoutEndsAt);
        Assert.Equal(5, account.FailedLoginAttemptCount);
        Assert.Equal(1, account.LockoutCount);
    }

    // Sans cette vérification, une réponse plus rapide révélerait que le compte existe et qu'il est bloqué.
    [Fact]
    public async Task PasswordIsStillComparedOnceDuringTheLockout()
    {
        AddAccount();
        await TryWrongPasswordAsync(5);
        var comparedCountBefore = passwordHasher.VerifiedPasswordCount;

        await LogInAsync(AccountEmail, WrongPassword);

        Assert.Equal(comparedCountBefore + 1, passwordHasher.VerifiedPasswordCount);
    }

    // Les essais pendant le blocage ne prolongent rien, mais ils restent visibles dans le journal.
    [Fact]
    public async Task AttemptsDuringTheLockoutAreNotCountedButAreWrittenInTheJournal()
    {
        var account = AddAccount();
        await TryWrongPasswordAsync(5);
        var savedCountBefore = accountRepository.LoginCounterSaveCount;

        await TryWrongPasswordAsync(3);

        Assert.Equal(savedCountBefore, accountRepository.LoginCounterSaveCount);
        Assert.Equal(1, account.LockoutCount);
        Assert.Equal(1, CountSecurityEvents(SecurityEventType.Lockout, SecurityEventResult.Success));
        Assert.Equal(8, CountSecurityEvents(SecurityEventType.Login, SecurityEventResult.Failure));
    }

    [Fact]
    public async Task AccountOpensAgainAtTheExactEndOfTheLockout()
    {
        AddAccount();
        await TryWrongPasswordAsync(5);

        clock.Advance(TimeSpan.FromMinutes(15));
        var result = await LogInAsync(AccountEmail, AccountPassword);

        Assert.IsType<AccountLoginResult.LoggedIn>(result);
    }

    [Fact]
    public async Task AccountStaysLockedJustBeforeTheEndOfTheLockout()
    {
        AddAccount();
        await TryWrongPasswordAsync(5);

        clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1));
        var result = await LogInAsync(AccountEmail, AccountPassword);

        AssertGenericRefusal(result);
    }

    [Fact]
    public async Task AfterTheLockoutFiveNewWrongPasswordsLockTheAccountForThirtyMinutes()
    {
        var account = AddAccount();
        await TryWrongPasswordAsync(5);
        clock.Advance(TimeSpan.FromMinutes(15));

        await TryWrongPasswordAsync(4);
        Assert.Equal(4, account.FailedLoginAttemptCount);
        Assert.Null(account.LockoutEndsAt);

        await TryWrongPasswordAsync(1);

        Assert.Equal(2, account.LockoutCount);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddMinutes(30), account.LockoutEndsAt);
        Assert.Equal(2, CountSecurityEvents(SecurityEventType.Lockout, SecurityEventResult.Success));
    }

    // C'est ce qui limite vraiment les requêtes simultanées : l'essai doit déjà être enregistré quand le mot de passe est comparé.
    [Fact]
    public async Task AttemptIsAlreadySavedWhenThePasswordIsCompared()
    {
        AddAccount();
        var savedCountAtComparison = -1;
        passwordHasher.OnPasswordVerified = () =>
            savedCountAtComparison = accountRepository.LoginCounterSaveCount;

        await LogInAsync(AccountEmail, WrongPassword);

        Assert.Equal(1, savedCountAtComparison);
    }

    // --- Requêtes simultanées et pannes

    // Une autre requête a compté son essai au même instant : aucun des deux essais ne doit se perdre.
    [Fact]
    public async Task ConflictWhileCountingRereadsTheAccountAndLosesNoAttempt()
    {
        var account = AddAccount();
        accountRepository.LoginCounterConflicts = 1;

        await LogInAsync(AccountEmail, WrongPassword);

        Assert.Equal(2, account.FailedLoginAttemptCount);
        Assert.Equal(1, accountRepository.LoginCounterSaveCount);
    }

    // Si l'essai n'a pas pu être compté, le mot de passe ne donne pas de session, même s'il est bon.
    [Fact]
    public async Task TooManyConflictsWhileCountingRefuseTheLoginEvenWithTheRightPassword()
    {
        AddAccount();
        accountRepository.LoginCounterConflicts = 1000;

        var result = await LogInAsync(AccountEmail, AccountPassword);

        AssertGenericRefusal(result);
        Assert.Equal(990, accountRepository.LoginCounterConflicts);
    }

    // Une autre requête a changé le compte pendant le hachage : le bon mot de passe doit quand même connecter.
    [Fact]
    public async Task ConflictWhileSavingTheLoginTriesAgainAndStillLogsIn()
    {
        AddAccount();
        sessionRepository.LoginConflicts = 3;

        var result = await LogInAsync(AccountEmail, AccountPassword);

        Assert.IsType<AccountLoginResult.LoggedIn>(result);
        Assert.Single(sessionRepository.SavedSessions);
        Assert.Equal(1, CountSecurityEvents(SecurityEventType.Login, SecurityEventResult.Success));
    }

    [Fact]
    public async Task TooManyConflictsWhileSavingTheLoginRefuseTheLogin()
    {
        AddAccount();
        sessionRepository.LoginConflicts = 1000;

        var result = await LogInAsync(AccountEmail, AccountPassword);

        AssertGenericRefusal(result);
        Assert.Equal(990, sessionRepository.LoginConflicts);
        Assert.Equal(0, CountSecurityEvents(SecurityEventType.Login, SecurityEventResult.Success));
    }

    [Fact]
    public async Task AccountErasedBeforeTheAttemptIsCountedIsRefused()
    {
        AddAccount();
        accountRepository.SearchesBeforeAccountDisappears = 0;

        var result = await LogInAsync(AccountEmail, AccountPassword);

        AssertGenericRefusal(result);
    }

    [Fact]
    public async Task AccountErasedBeforeTheLoginIsSavedIsRefused()
    {
        AddAccount();
        accountRepository.SearchesBeforeAccountDisappears = 1;

        var result = await LogInAsync(AccountEmail, AccountPassword);

        AssertGenericRefusal(result);
    }

    // Une panne de la base ne doit jamais ouvrir de session.
    [Fact]
    public async Task DatabaseFailureWhileSavingTheLoginIsNotHiddenAndOpensNoSession()
    {
        AddAccount();
        sessionRepository.IsDown = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LogInAsync(AccountEmail, AccountPassword));

        Assert.Empty(sessionRepository.SavedSessions);
    }

    // Une récupération passe pendant le hachage : le mot de passe vérifié n'est plus le bon, aucune session ne doit s'ouvrir (SS §3).
    [Fact]
    public async Task PasswordChangedWhileItIsBeingCheckedOpensNoSession()
    {
        var account = AddAccount();
        passwordHasher.OnPasswordVerified = () =>
            account.ReplacePasswordHash("empreinte-du-nouveau-mot-de-passe");

        var result = await LogInAsync(AccountEmail, AccountPassword);

        // La comparaison dit vrai, car elle porte sur l'ancienne empreinte : seul le contrôle qui la suit refuse la session.
        AssertGenericRefusal(result);
    }

    // --- E-mail de déblocage

    [Fact]
    public async Task FifthWrongPasswordAsksForOneUnlockEmailWithTheAccountAndIpAddress()
    {
        var account = AddAccount();

        await TryWrongPasswordAsync(5);

        var unlockRequest = Assert.IsType<WalletFlow.Application.AccountRecovery.BackgroundEmailRequest.AccountUnlock>(Assert.Single(emailQueue.AddedRequests));
        Assert.Equal(account.Id, unlockRequest.AccountId);
        Assert.Equal(IpAddress, unlockRequest.IpAddress);
    }

    [Fact]
    public async Task FourWrongPasswordsAskForNoUnlockEmail()
    {
        AddAccount();

        await TryWrongPasswordAsync(4);

        Assert.Empty(emailQueue.AddedRequests);
    }

    // SS §2 : un seul e-mail par blocage, pour qu'un attaquant ne puisse pas inonder la boîte de la victime.
    [Fact]
    public async Task AttemptsDuringTheLockoutAskForNoOtherUnlockEmail()
    {
        AddAccount();
        await TryWrongPasswordAsync(5);

        await TryWrongPasswordAsync(3);
        await LogInAsync(AccountEmail, AccountPassword);

        Assert.Single(emailQueue.AddedRequests);
    }

    [Fact]
    public async Task EveryNewLockoutAsksForItsOwnUnlockEmail()
    {
        AddAccount();
        await TryWrongPasswordAsync(5);
        clock.Advance(TimeSpan.FromMinutes(15));

        await TryWrongPasswordAsync(5);

        Assert.Equal(2, emailQueue.AddedRequests.Count);
    }

    // Le bon mot de passe au 5e essai annule le blocage : il n'y a rien à débloquer.
    [Fact]
    public async Task RightPasswordOnTheFifthAttemptAsksForNoUnlockEmail()
    {
        AddAccount();
        await TryWrongPasswordAsync(4);

        await LogInAsync(AccountEmail, AccountPassword);

        Assert.Empty(emailQueue.AddedRequests);
    }

    // SS §2 : un compte non confirmé n'est jamais bloqué, et aucun e-mail de déblocage ne part.
    [Fact]
    public async Task UnconfirmedAccountNeverAsksForAnUnlockEmail()
    {
        AddAccount(isEmailConfirmed: false);

        await TryWrongPasswordAsync(6);

        Assert.Empty(emailQueue.AddedRequests);
    }

    // --- Appareils

    [Fact]
    public async Task FirstLoginRegistersTheDeviceWithOnlyTheHashOfItsToken()
    {
        var account = AddAccount();

        var result = await LogInAsync(AccountEmail, AccountPassword);

        var device = Assert.Single(sessionRepository.SavedDevices);
        Assert.Equal(account.Id, device.UserId);
        Assert.Equal(tokenGenerator.HashToken(Assert.IsType<AccountLoginResult.LoggedIn>(result).DeviceToken), device.DeviceToken);
        Assert.NotEqual(Assert.IsType<AccountLoginResult.LoggedIn>(result).DeviceToken, device.DeviceToken);
        Assert.NotEqual(Assert.IsType<AccountLoginResult.LoggedIn>(result).SessionToken, Assert.IsType<AccountLoginResult.LoggedIn>(result).DeviceToken);
    }

    // Fait une connexion réussie avec ces cookies déjà présents dans le navigateur.
    private async Task<AccountLoginResult> LogInWithCookiesAsync(string? deviceToken, string? sessionToken)
    {
        return await CreateLoginService().LogInAsync(new LoginAttempt
        {
            EmailAddress = AccountEmail,
            Password = AccountPassword,
            IpAddress = IpAddress,
            DeviceToken = deviceToken,
            SessionToken = sessionToken
        });
    }

    [Fact]
    public async Task KnownDeviceKeepsItsTokenAndAddsNothing()
    {
        AddAccount();
        var firstResult = await LogInWithCookiesAsync(null, null);

        var secondResult = await LogInWithCookiesAsync(Assert.IsType<AccountLoginResult.LoggedIn>(firstResult).DeviceToken, null);

        Assert.Equal(Assert.IsType<AccountLoginResult.LoggedIn>(firstResult).DeviceToken, Assert.IsType<AccountLoginResult.LoggedIn>(secondResult).DeviceToken);
        Assert.Single(sessionRepository.SavedDevices);
    }

    [Fact]
    public async Task UnknownDeviceTokenIsReplacedByANewOne()
    {
        AddAccount();

        var result = await LogInWithCookiesAsync("jeton-d-appareil-invente", null);

        Assert.NotEqual("jeton-d-appareil-invente", Assert.IsType<AccountLoginResult.LoggedIn>(result).DeviceToken);
        Assert.Single(sessionRepository.SavedDevices);
    }

    // Le jeton d'un autre compte ne rend pas l'appareil connu pour celui-ci.
    [Fact]
    public async Task DeviceTokenOfAnotherAccountIsReplacedByANewOne()
    {
        AddAccount();
        var otherDeviceToken = "jeton-d-appareil-d-un-autre-compte";
        sessionRepository.SavedDevices.Add(new Device
        {
            DeviceToken = tokenGenerator.HashToken(otherDeviceToken),
            UserId = Guid.NewGuid()
        });

        var result = await LogInWithCookiesAsync(otherDeviceToken, null);

        Assert.NotEqual(otherDeviceToken, Assert.IsType<AccountLoginResult.LoggedIn>(result).DeviceToken);
        Assert.Equal(2, sessionRepository.SavedDevices.Count);
    }

    // --- Ancienne session du navigateur

    [Fact]
    public async Task SuccessfulLoginClosesTheOldSessionOfThisBrowser()
    {
        AddAccount();
        var firstResult = await LogInWithCookiesAsync(null, null);

        var secondResult = await LogInWithCookiesAsync(null, Assert.IsType<AccountLoginResult.LoggedIn>(firstResult).SessionToken);

        var openSession = Assert.Single(sessionRepository.SavedSessions);
        Assert.Equal(tokenGenerator.HashToken(Assert.IsType<AccountLoginResult.LoggedIn>(secondResult).SessionToken), openSession.Id);
    }

    // Un mauvais mot de passe ne doit pas déconnecter le navigateur.
    [Fact]
    public async Task FailedLoginKeepsTheOldSessionOfThisBrowser()
    {
        AddAccount();
        var firstResult = await LogInWithCookiesAsync(null, null);

        await CreateLoginService().LogInAsync(new LoginAttempt
        {
            EmailAddress = AccountEmail,
            Password = WrongPassword,
            SessionToken = Assert.IsType<AccountLoginResult.LoggedIn>(firstResult).SessionToken
        });

        Assert.Single(sessionRepository.SavedSessions);
    }

    // --- Normalisation

    [Fact]
    public async Task PasswordTypedWithDecomposedAccentMatchesThePasswordChosenWithComposedAccent()
    {
        var account = AddAccount();
        account.ReplacePasswordHash(FakePasswordHasher.HashPrefix + "Mot-De-Passé-1");

        var result = await LogInAsync(AccountEmail, "Mot-De-Passe\u0301-1");

        Assert.IsType<AccountLoginResult.LoggedIn>(result);
    }

    // Ces caractères font lever une erreur à .NET : le visiteur doit recevoir le refus normal, pas une erreur 500.
    [Theory]
    [InlineData(0xFFFE)]
    [InlineData(0xD800)]
    public async Task PasswordThatCannotBeNormalizedIsRefusedWithTheGenericMessage(int unnormalizableCharacter)
    {
        AddAccount();

        var result = await LogInAsync(
            AccountEmail,
            AccountPassword + (char)unnormalizableCharacter);

        AssertGenericRefusal(result);
    }
}
