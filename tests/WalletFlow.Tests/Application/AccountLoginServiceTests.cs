using System.Net;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le cas d'utilisation de la connexion (GR Auth, SS §2, §3 et §11), avec de faux outils.
// Seuls les jetons sont les vrais : il faut que chaque jeton soit différent pour comparer session et appareil.
public class AccountLoginServiceTests
{
    private const string EmailAddressOfTheAccountInTheseTests = "utilisateur@exemple.test";
    private const string PasswordOfTheAccountInTheseTests = "Mot-De-Passe-Valide-1";
    private const string WrongPasswordTypedInTheseTests = "Mauvais-Mot-De-Passe-2";

    private static readonly IPAddress IpAddressOfTheVisitorInTheseTests = IPAddress.Parse("203.0.113.7");

    private readonly FakeUserAccountRepository repositoryThatKeepsAccountsInMemory = new FakeUserAccountRepository();
    private readonly FakeSecurityEventRepository repositoryThatKeepsSecurityEventsInMemory = new FakeSecurityEventRepository();
    private readonly FakeUserSessionRepository repositoryThatKeepsSessionsInMemory;
    private readonly FakeTwoFactorAuthenticationRepository repositoryThatKeepsTwoFactorDataInMemory;
    private readonly InMemoryLoginsWaitingForTheirCodeStore storeOfTheLoginsWaitingForTheirCode;
    private readonly FakeBackgroundEmailRequestQueue queueThatKeepsTheEmailRequestsInMemory = new FakeBackgroundEmailRequestQueue();
    private readonly FakePasswordHasher hasherThatCountsTheComparedPasswords = new FakePasswordHasher();
    private readonly RandomSecureTokenGenerator realGeneratorOfTheTokensInTheseTests = new RandomSecureTokenGenerator();
    private readonly FakeTimeProviderForTests clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();

    // Relie les faux accès au faux journal de sécurité.
    public AccountLoginServiceTests()
    {
        repositoryThatKeepsSessionsInMemory = new FakeUserSessionRepository(repositoryThatKeepsSecurityEventsInMemory);
        repositoryThatKeepsTwoFactorDataInMemory = new FakeTwoFactorAuthenticationRepository(repositoryThatKeepsAccountsInMemory, repositoryThatKeepsSecurityEventsInMemory);
        storeOfTheLoginsWaitingForTheirCode = new InMemoryLoginsWaitingForTheirCodeStore(clockThatOnlyMovesWhenTestsAskIt);
    }

    // Crée le service à tester, avec ses faux outils.
    private AccountLoginService CreateLoginServiceWithFakeTools()
    {
        return new AccountLoginService(
            repositoryThatKeepsAccountsInMemory,
            repositoryThatKeepsSecurityEventsInMemory,
            queueThatKeepsTheEmailRequestsInMemory,
            hasherThatCountsTheComparedPasswords,
            realGeneratorOfTheTokensInTheseTests,
            storeOfTheLoginsWaitingForTheirCode,
            new UserSessionOpeningService(
                repositoryThatKeepsAccountsInMemory,
                repositoryThatKeepsSessionsInMemory,
                repositoryThatKeepsTwoFactorDataInMemory,
                realGeneratorOfTheTokensInTheseTests,
                clockThatOnlyMovesWhenTestsAskIt),
            clockThatOnlyMovesWhenTestsAskIt);
    }

    // Ajoute à la fausse base un compte avec l'adresse de ces tests.
    private User AddAccountToTheFakeDatabase(bool emailAddressOfTheAccountIsConfirmed = true)
    {
        var accountAddedToTheFakeDatabase = new User
        {
            Email = EmailAddressOfTheAccountInTheseTests,
            PasswordHash = FakePasswordHasher.PrefixOfTheFakePasswordHashes + PasswordOfTheAccountInTheseTests,
            IsEmailConfirmed = emailAddressOfTheAccountIsConfirmed
        };
        repositoryThatKeepsAccountsInMemory.SavedUsers.Add(accountAddedToTheFakeDatabase);

        return accountAddedToTheFakeDatabase;
    }

    // Fait une tentative de connexion, sans cookie déjà présent.
    private async Task<AccountLoginResult> TryToLogInWithThisEmailAddressAndPasswordAsync(string emailAddressTypedInThisTest, string passwordTypedByTheVisitorInThisTest)
    {
        return await CreateLoginServiceWithFakeTools().LogVisitorInWithEmailAddressAndPasswordAsync(new LoginAttemptFromVisitor
        {
            EmailAddressTypedByTheVisitor = emailAddressTypedInThisTest,
            PasswordTextTypedByTheVisitor = passwordTypedByTheVisitorInThisTest,
            IpAddressOfTheVisitor = IpAddressOfTheVisitorInTheseTests
        });
    }

    // Fait cette série de mauvais essais sur le compte de ces tests.
    private async Task TryWrongPasswordThisNumberOfTimesAsync(int numberOfWrongPasswordsToTry)
    {
        for (var numberOfTheWrongPasswordBeingTried = 1; numberOfTheWrongPasswordBeingTried <= numberOfWrongPasswordsToTry; numberOfTheWrongPasswordBeingTried++)
        {
            await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, WrongPasswordTypedInTheseTests);
        }
    }

    // Vérifie que la connexion est refusée avec le message générique, sans session.
    private void CheckThatLoginWasRefusedWithTheGenericMessage(AccountLoginResult resultOfTheLoginToCheck)
    {
        Assert.False(resultOfTheLoginToCheck.IsLoggedIn);
        Assert.Equal("Identifiants incorrects", resultOfTheLoginToCheck.WrongCredentialsErrorMessage);
        Assert.Null(resultOfTheLoginToCheck.EmailAddressNotConfirmedErrorMessage);
        Assert.Null(resultOfTheLoginToCheck.DeviceTokenToPutInTheCookie);
        Assert.Empty(repositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // Compte les événements de ce type et de ce résultat dans le faux journal.
    private int CountSecurityEventsOfThisTypeAndResult(SecurityEventType typeOfTheEventsToCount, SecurityEventResult resultOfTheEventsToCount)
    {
        var numberOfEventsFoundInTheJournal = 0;

        foreach (var securityEventKeptInTheJournal in repositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents)
        {
            if (securityEventKeptInTheJournal.Type == typeOfTheEventsToCount && securityEventKeptInTheJournal.Result == resultOfTheEventsToCount)
            {
                numberOfEventsFoundInTheJournal = numberOfEventsFoundInTheJournal + 1;
            }
        }

        return numberOfEventsFoundInTheJournal;
    }

    // --- Connexion réussie

    [Fact]
    public async Task RightPasswordOpensSessionAndStoresOnlyTheHashOfItsToken()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase();

        var resultOfTheLoginOfTheAccount = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        Assert.True(resultOfTheLoginOfTheAccount.IsLoggedIn);
        var sessionSavedInMemoryForTheAccount = Assert.Single(repositoryThatKeepsSessionsInMemory.SavedSessions);
        Assert.Equal(accountBeingTestedInThisLogin.Id, sessionSavedInMemoryForTheAccount.UserId);
        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime, sessionSavedInMemoryForTheAccount.LastActivityAt);
        Assert.NotEqual(resultOfTheLoginOfTheAccount.SessionTokenToPutInTheCookie, sessionSavedInMemoryForTheAccount.Id);
        Assert.Equal(realGeneratorOfTheTokensInTheseTests.ConvertSecureTokenIntoItsHash(resultOfTheLoginOfTheAccount.SessionTokenToPutInTheCookie!), sessionSavedInMemoryForTheAccount.Id);
    }

    // SS §3 : un nouvel identifiant à chaque connexion, pour qu'un attaquant ne puisse pas imposer le sien.
    [Fact]
    public async Task EveryLoginGetsANewSessionToken()
    {
        AddAccountToTheFakeDatabase();

        var resultOfTheFirstLoginOfTheAccount = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);
        var resultOfTheSecondLoginOfTheAccount = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        Assert.NotEqual(resultOfTheFirstLoginOfTheAccount.SessionTokenToPutInTheCookie, resultOfTheSecondLoginOfTheAccount.SessionTokenToPutInTheCookie);
        Assert.Equal(2, repositoryThatKeepsSessionsInMemory.SavedSessions.Count);
    }

    // L'adresse tapée avec des majuscules et des espaces désigne le même compte.
    [Fact]
    public async Task EmailAddressIsSearchedInItsStoredForm()
    {
        AddAccountToTheFakeDatabase();

        var resultOfTheLoginOfTheAccount = await TryToLogInWithThisEmailAddressAndPasswordAsync(" Utilisateur@Exemple.TEST ", PasswordOfTheAccountInTheseTests);

        Assert.True(resultOfTheLoginOfTheAccount.IsLoggedIn);
    }

    [Fact]
    public async Task SuccessfulLoginResetsTheCountersOfTheAccount()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase();
        accountBeingTestedInThisLogin.FailedLoginAttemptCount = 3;
        accountBeingTestedInThisLogin.LockoutCount = 2;

        await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        Assert.Equal(0, accountBeingTestedInThisLogin.FailedLoginAttemptCount);
        Assert.Equal(0, accountBeingTestedInThisLogin.LockoutCount);
    }

    // Le 5e essai est compté avant la vérification : avec le bon mot de passe, le compte ne doit pas rester bloqué.
    [Fact]
    public async Task RightPasswordOnTheFifthAttemptLogsInAndLeavesNoLockout()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase();
        await TryWrongPasswordThisNumberOfTimesAsync(4);

        var resultOfTheLoginOfTheAccount = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        Assert.True(resultOfTheLoginOfTheAccount.IsLoggedIn);
        Assert.Null(accountBeingTestedInThisLogin.LockoutEndsAt);
        Assert.Equal(0, accountBeingTestedInThisLogin.LockoutCount);
        Assert.Equal(0, CountSecurityEventsOfThisTypeAndResult(SecurityEventType.Lockout, SecurityEventResult.Success));
    }

    [Fact]
    public async Task SuccessfulLoginIsWrittenInTheJournalWithItsDateUserAndIpAddress()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase();

        await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        var securityEventOfTheSuccessfulUserLogin = Assert.Single(repositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.Login, securityEventOfTheSuccessfulUserLogin.Type);
        Assert.Equal(SecurityEventResult.Success, securityEventOfTheSuccessfulUserLogin.Result);
        Assert.Equal(accountBeingTestedInThisLogin.Id, securityEventOfTheSuccessfulUserLogin.UserId);
        Assert.Equal(IpAddressOfTheVisitorInTheseTests, securityEventOfTheSuccessfulUserLogin.IpAddress);
        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime, securityEventOfTheSuccessfulUserLogin.OccurredAt);
    }

    // --- Mauvais identifiants

    [Fact]
    public async Task WrongPasswordGetsTheGenericMessageAndCountsOneAttempt()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase();

        var resultOfTheLoginOfTheAccount = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, WrongPasswordTypedInTheseTests);

        CheckThatLoginWasRefusedWithTheGenericMessage(resultOfTheLoginOfTheAccount);
        Assert.Equal(1, accountBeingTestedInThisLogin.FailedLoginAttemptCount);
        Assert.Equal(1, repositoryThatKeepsAccountsInMemory.NumberOfLoginCountersSaved);

        var securityEventOfTheFailedLogin = Assert.Single(repositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.Login, securityEventOfTheFailedLogin.Type);
        Assert.Equal(SecurityEventResult.Failure, securityEventOfTheFailedLogin.Result);
        Assert.Equal(accountBeingTestedInThisLogin.Id, securityEventOfTheFailedLogin.UserId);
        Assert.Equal(IpAddressOfTheVisitorInTheseTests, securityEventOfTheFailedLogin.IpAddress);
    }

    // Sans ce calcul, une réponse plus rapide révélerait quelles adresses n'ont pas de compte (SS §2).
    [Fact]
    public async Task UnknownEmailAddressGetsTheSameMessageAndStillComputesOneHash()
    {
        var resultOfTheLoginWithUnknownAddress = await TryToLogInWithThisEmailAddressAndPasswordAsync("inconnu@exemple.test", PasswordOfTheAccountInTheseTests);

        CheckThatLoginWasRefusedWithTheGenericMessage(resultOfTheLoginWithUnknownAddress);
        Assert.Equal(1, hasherThatCountsTheComparedPasswords.NumberOfPasswordsHashed);

        var securityEventOfTheFailedLogin = Assert.Single(repositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
        Assert.Equal(SecurityEventResult.Failure, securityEventOfTheFailedLogin.Result);
        Assert.Null(securityEventOfTheFailedLogin.UserId);
        Assert.Equal(IpAddressOfTheVisitorInTheseTests, securityEventOfTheFailedLogin.IpAddress);
    }

    // Une adresse mal formée ne doit jamais arriver à la base : le caractère nul y provoquerait une erreur.
    [Theory]
    [InlineData("sans-arobase")]
    [InlineData("nul\0caractere@exemple.test")]
    [InlineData("")]
    public async Task MalformedEmailAddressIsNeverSentToTheDatabase(string malformedEmailAddressTypedInThisTest)
    {
        var resultOfTheLoginWithMalformedAddress = await TryToLogInWithThisEmailAddressAndPasswordAsync(malformedEmailAddressTypedInThisTest, PasswordOfTheAccountInTheseTests);

        CheckThatLoginWasRefusedWithTheGenericMessage(resultOfTheLoginWithMalformedAddress);
        Assert.Equal(0, repositoryThatKeepsAccountsInMemory.NumberOfSearchesByEmailAddress);
        Assert.Equal(1, hasherThatCountsTheComparedPasswords.NumberOfPasswordsHashed);
    }

    // --- Compte non confirmé

    // Le message qui invite à confirmer n'apparaît qu'avec le bon mot de passe (GR Auth, SS §2).
    [Fact]
    public async Task UnconfirmedAccountWithRightPasswordIsInvitedToConfirmWithoutSession()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase(emailAddressOfTheAccountIsConfirmed: false);

        var resultOfTheLoginOfTheAccount = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        Assert.False(resultOfTheLoginOfTheAccount.IsLoggedIn);
        Assert.Equal(AccountLoginService.ErrorMessageForEmailAddressNotConfirmed, resultOfTheLoginOfTheAccount.EmailAddressNotConfirmedErrorMessage);
        Assert.Null(resultOfTheLoginOfTheAccount.WrongCredentialsErrorMessage);
        Assert.Empty(repositoryThatKeepsSessionsInMemory.SavedSessions);
        Assert.Equal(0, accountBeingTestedInThisLogin.FailedLoginAttemptCount);
        Assert.Equal(1, CountSecurityEventsOfThisTypeAndResult(SecurityEventType.Login, SecurityEventResult.Failure));
    }

    [Fact]
    public async Task UnconfirmedAccountWithWrongPasswordGetsTheGenericMessage()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase(emailAddressOfTheAccountIsConfirmed: false);

        var resultOfTheLoginOfTheAccount = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, WrongPasswordTypedInTheseTests);

        CheckThatLoginWasRefusedWithTheGenericMessage(resultOfTheLoginOfTheAccount);
        var securityEventOfTheFailedLogin = Assert.Single(repositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
        Assert.Equal(SecurityEventResult.Failure, securityEventOfTheFailedLogin.Result);
        Assert.Equal(accountBeingTestedInThisLogin.Id, securityEventOfTheFailedLogin.UserId);
    }

    // Un compte non confirmé n'est jamais bloqué : ses essais ne comptent pas (SS §2).
    [Fact]
    public async Task UnconfirmedAccountIsNeverLockedHoweverManyWrongPasswordsAreTried()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase(emailAddressOfTheAccountIsConfirmed: false);

        await TryWrongPasswordThisNumberOfTimesAsync(6);

        Assert.Equal(0, accountBeingTestedInThisLogin.FailedLoginAttemptCount);
        Assert.Equal(0, accountBeingTestedInThisLogin.LockoutCount);
        Assert.Null(accountBeingTestedInThisLogin.LockoutEndsAt);
        Assert.Equal(0, repositoryThatKeepsAccountsInMemory.NumberOfLoginCountersSaved);
        Assert.Equal(0, CountSecurityEventsOfThisTypeAndResult(SecurityEventType.Lockout, SecurityEventResult.Success));
    }

    // --- Blocage

    [Fact]
    public async Task FifthWrongPasswordLocksTheAccountAndWritesOneLockoutEvent()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase();

        await TryWrongPasswordThisNumberOfTimesAsync(5);

        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddMinutes(15), accountBeingTestedInThisLogin.LockoutEndsAt);
        Assert.Equal(1, CountSecurityEventsOfThisTypeAndResult(SecurityEventType.Lockout, SecurityEventResult.Success));
        Assert.Equal(5, CountSecurityEventsOfThisTypeAndResult(SecurityEventType.Login, SecurityEventResult.Failure));
    }

    [Fact]
    public async Task FourWrongPasswordsDoNotWriteAnyLockoutEvent()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase();

        await TryWrongPasswordThisNumberOfTimesAsync(4);

        Assert.Null(accountBeingTestedInThisLogin.LockoutEndsAt);
        Assert.Equal(0, CountSecurityEventsOfThisTypeAndResult(SecurityEventType.Lockout, SecurityEventResult.Success));
    }

    // Pendant le blocage, le message reste générique, même avec le bon mot de passe (SS §2).
    [Fact]
    public async Task RightPasswordDuringTheLockoutGetsTheGenericMessageWithoutChangingTheAccount()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase();
        await TryWrongPasswordThisNumberOfTimesAsync(5);
        var endOfTheLockoutBeforeThisAttempt = accountBeingTestedInThisLogin.LockoutEndsAt;

        var resultOfTheLoginDuringTheLockout = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        CheckThatLoginWasRefusedWithTheGenericMessage(resultOfTheLoginDuringTheLockout);
        Assert.Equal(endOfTheLockoutBeforeThisAttempt, accountBeingTestedInThisLogin.LockoutEndsAt);
        Assert.Equal(5, accountBeingTestedInThisLogin.FailedLoginAttemptCount);
        Assert.Equal(1, accountBeingTestedInThisLogin.LockoutCount);
    }

    // Sans cette vérification, une réponse plus rapide révélerait que le compte existe et qu'il est bloqué.
    [Fact]
    public async Task PasswordIsStillComparedOnceDuringTheLockout()
    {
        AddAccountToTheFakeDatabase();
        await TryWrongPasswordThisNumberOfTimesAsync(5);
        var numberOfPasswordsComparedBeforeThisAttempt = hasherThatCountsTheComparedPasswords.NumberOfPasswordsCompared;

        await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, WrongPasswordTypedInTheseTests);

        Assert.Equal(numberOfPasswordsComparedBeforeThisAttempt + 1, hasherThatCountsTheComparedPasswords.NumberOfPasswordsCompared);
    }

    // Les essais pendant le blocage ne prolongent rien, mais ils restent visibles dans le journal.
    [Fact]
    public async Task AttemptsDuringTheLockoutAreNotCountedButAreWrittenInTheJournal()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase();
        await TryWrongPasswordThisNumberOfTimesAsync(5);
        var numberOfCountersSavedBeforeTheLockout = repositoryThatKeepsAccountsInMemory.NumberOfLoginCountersSaved;

        await TryWrongPasswordThisNumberOfTimesAsync(3);

        Assert.Equal(numberOfCountersSavedBeforeTheLockout, repositoryThatKeepsAccountsInMemory.NumberOfLoginCountersSaved);
        Assert.Equal(1, accountBeingTestedInThisLogin.LockoutCount);
        Assert.Equal(1, CountSecurityEventsOfThisTypeAndResult(SecurityEventType.Lockout, SecurityEventResult.Success));
        Assert.Equal(8, CountSecurityEventsOfThisTypeAndResult(SecurityEventType.Login, SecurityEventResult.Failure));
    }

    [Fact]
    public async Task AccountOpensAgainAtTheExactEndOfTheLockout()
    {
        AddAccountToTheFakeDatabase();
        await TryWrongPasswordThisNumberOfTimesAsync(5);

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(15));
        var resultOfTheLoginAfterTheLockout = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        Assert.True(resultOfTheLoginAfterTheLockout.IsLoggedIn);
    }

    [Fact]
    public async Task AccountStaysLockedJustBeforeTheEndOfTheLockout()
    {
        AddAccountToTheFakeDatabase();
        await TryWrongPasswordThisNumberOfTimesAsync(5);

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(15) - TimeSpan.FromSeconds(1));
        var resultOfTheLoginBeforeTheEnd = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        CheckThatLoginWasRefusedWithTheGenericMessage(resultOfTheLoginBeforeTheEnd);
    }

    [Fact]
    public async Task AfterTheLockoutFiveNewWrongPasswordsLockTheAccountForThirtyMinutes()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase();
        await TryWrongPasswordThisNumberOfTimesAsync(5);
        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(15));

        await TryWrongPasswordThisNumberOfTimesAsync(4);
        Assert.Equal(4, accountBeingTestedInThisLogin.FailedLoginAttemptCount);
        Assert.Null(accountBeingTestedInThisLogin.LockoutEndsAt);

        await TryWrongPasswordThisNumberOfTimesAsync(1);

        Assert.Equal(2, accountBeingTestedInThisLogin.LockoutCount);
        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddMinutes(30), accountBeingTestedInThisLogin.LockoutEndsAt);
        Assert.Equal(2, CountSecurityEventsOfThisTypeAndResult(SecurityEventType.Lockout, SecurityEventResult.Success));
    }

    // C'est ce qui limite vraiment les requêtes simultanées : l'essai doit déjà être enregistré quand le mot de passe est comparé.
    [Fact]
    public async Task AttemptIsAlreadySavedWhenThePasswordIsCompared()
    {
        AddAccountToTheFakeDatabase();
        var numberOfCountersSavedWhenThePasswordIsCompared = -1;
        hasherThatCountsTheComparedPasswords.ActionRunWhenOnePasswordIsCompared = () =>
            numberOfCountersSavedWhenThePasswordIsCompared = repositoryThatKeepsAccountsInMemory.NumberOfLoginCountersSaved;

        await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, WrongPasswordTypedInTheseTests);

        Assert.Equal(1, numberOfCountersSavedWhenThePasswordIsCompared);
    }

    // --- Requêtes simultanées et pannes

    // Une autre requête a compté son essai au même instant : aucun des deux essais ne doit se perdre.
    [Fact]
    public async Task ConflictWhileCountingRereadsTheAccountAndLosesNoAttempt()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.NumberOfConflictsBeforeTheLoginCountersAreSaved = 1;

        await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, WrongPasswordTypedInTheseTests);

        Assert.Equal(2, accountBeingTestedInThisLogin.FailedLoginAttemptCount);
        Assert.Equal(1, repositoryThatKeepsAccountsInMemory.NumberOfLoginCountersSaved);
    }

    // Si l'essai n'a pas pu être compté, le mot de passe ne donne pas de session, même s'il est bon.
    [Fact]
    public async Task TooManyConflictsWhileCountingRefuseTheLoginEvenWithTheRightPassword()
    {
        AddAccountToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.NumberOfConflictsBeforeTheLoginCountersAreSaved = 1000;

        var resultOfTheLoginDuringTheConflicts = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        CheckThatLoginWasRefusedWithTheGenericMessage(resultOfTheLoginDuringTheConflicts);
        Assert.Equal(990, repositoryThatKeepsAccountsInMemory.NumberOfConflictsBeforeTheLoginCountersAreSaved);
    }

    // Une autre requête a changé le compte pendant le hachage : le bon mot de passe doit quand même connecter.
    [Fact]
    public async Task ConflictWhileSavingTheLoginTriesAgainAndStillLogsIn()
    {
        AddAccountToTheFakeDatabase();
        repositoryThatKeepsSessionsInMemory.NumberOfConflictsBeforeTheLoginIsSaved = 3;

        var resultOfTheLoginDuringTheConflicts = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        Assert.True(resultOfTheLoginDuringTheConflicts.IsLoggedIn);
        Assert.Single(repositoryThatKeepsSessionsInMemory.SavedSessions);
        Assert.Equal(1, CountSecurityEventsOfThisTypeAndResult(SecurityEventType.Login, SecurityEventResult.Success));
    }

    [Fact]
    public async Task TooManyConflictsWhileSavingTheLoginRefuseTheLogin()
    {
        AddAccountToTheFakeDatabase();
        repositoryThatKeepsSessionsInMemory.NumberOfConflictsBeforeTheLoginIsSaved = 1000;

        var resultOfTheLoginDuringTheConflicts = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        CheckThatLoginWasRefusedWithTheGenericMessage(resultOfTheLoginDuringTheConflicts);
        Assert.Equal(990, repositoryThatKeepsSessionsInMemory.NumberOfConflictsBeforeTheLoginIsSaved);
        Assert.Equal(0, CountSecurityEventsOfThisTypeAndResult(SecurityEventType.Login, SecurityEventResult.Success));
    }

    [Fact]
    public async Task AccountErasedBeforeTheAttemptIsCountedIsRefused()
    {
        AddAccountToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.NumberOfSearchesByIdentifierBeforeTheAccountDisappears = 0;

        var resultOfTheLoginOfTheErasedAccount = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        CheckThatLoginWasRefusedWithTheGenericMessage(resultOfTheLoginOfTheErasedAccount);
    }

    [Fact]
    public async Task AccountErasedBeforeTheLoginIsSavedIsRefused()
    {
        AddAccountToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.NumberOfSearchesByIdentifierBeforeTheAccountDisappears = 1;

        var resultOfTheLoginOfTheErasedAccount = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        CheckThatLoginWasRefusedWithTheGenericMessage(resultOfTheLoginOfTheErasedAccount);
    }

    // Une panne de la base ne doit jamais ouvrir de session.
    [Fact]
    public async Task DatabaseFailureWhileSavingTheLoginIsNotHiddenAndOpensNoSession()
    {
        AddAccountToTheFakeDatabase();
        repositoryThatKeepsSessionsInMemory.DatabaseIsDownDuringThisTest = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests));

        Assert.Empty(repositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // Une récupération passe pendant le hachage : le mot de passe vérifié n'est plus le bon, aucune session ne doit s'ouvrir (SS §3).
    [Fact]
    public async Task PasswordChangedWhileItIsBeingCheckedOpensNoSession()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase();
        hasherThatCountsTheComparedPasswords.ActionRunWhenOnePasswordIsCompared = () =>
            accountBeingTestedInThisLogin.PasswordHash = "empreinte-du-nouveau-mot-de-passe";

        var resultOfTheLoginDuringTheRecovery = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        // La comparaison dit vrai, car elle porte sur l'ancienne empreinte : seul le contrôle qui la suit refuse la session.
        CheckThatLoginWasRefusedWithTheGenericMessage(resultOfTheLoginDuringTheRecovery);
    }

    // --- E-mail de déblocage

    [Fact]
    public async Task FifthWrongPasswordAsksForOneUnlockEmailWithTheAccountAndIpAddress()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase();

        await TryWrongPasswordThisNumberOfTimesAsync(5);

        var requestOfTheAccountUnlockEmail = Assert.Single(queueThatKeepsTheEmailRequestsInMemory.RequestsAddedToTheBackgroundQueue);
        Assert.Equal(WalletFlow.Application.AccountRecovery.BackgroundEmailRequestType.AccountUnlock, requestOfTheAccountUnlockEmail.TypeOfTheRequestedBackgroundEmail);
        Assert.Equal(accountBeingTestedInThisLogin.Id, requestOfTheAccountUnlockEmail.IdentifierOfTheAccountJustLocked);
        Assert.Equal(IpAddressOfTheVisitorInTheseTests, requestOfTheAccountUnlockEmail.IpAddressOfTheRequestingVisitor);
    }

    [Fact]
    public async Task FourWrongPasswordsAskForNoUnlockEmail()
    {
        AddAccountToTheFakeDatabase();

        await TryWrongPasswordThisNumberOfTimesAsync(4);

        Assert.Empty(queueThatKeepsTheEmailRequestsInMemory.RequestsAddedToTheBackgroundQueue);
    }

    // SS §2 : un seul e-mail par blocage, pour qu'un attaquant ne puisse pas inonder la boîte de la victime.
    [Fact]
    public async Task AttemptsDuringTheLockoutAskForNoOtherUnlockEmail()
    {
        AddAccountToTheFakeDatabase();
        await TryWrongPasswordThisNumberOfTimesAsync(5);

        await TryWrongPasswordThisNumberOfTimesAsync(3);
        await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        Assert.Single(queueThatKeepsTheEmailRequestsInMemory.RequestsAddedToTheBackgroundQueue);
    }

    [Fact]
    public async Task EveryNewLockoutAsksForItsOwnUnlockEmail()
    {
        AddAccountToTheFakeDatabase();
        await TryWrongPasswordThisNumberOfTimesAsync(5);
        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(15));

        await TryWrongPasswordThisNumberOfTimesAsync(5);

        Assert.Equal(2, queueThatKeepsTheEmailRequestsInMemory.RequestsAddedToTheBackgroundQueue.Count);
    }

    // Le bon mot de passe au 5e essai annule le blocage : il n'y a rien à débloquer.
    [Fact]
    public async Task RightPasswordOnTheFifthAttemptAsksForNoUnlockEmail()
    {
        AddAccountToTheFakeDatabase();
        await TryWrongPasswordThisNumberOfTimesAsync(4);

        await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        Assert.Empty(queueThatKeepsTheEmailRequestsInMemory.RequestsAddedToTheBackgroundQueue);
    }

    // SS §2 : un compte non confirmé n'est jamais bloqué, et aucun e-mail de déblocage ne part.
    [Fact]
    public async Task UnconfirmedAccountNeverAsksForAnUnlockEmail()
    {
        AddAccountToTheFakeDatabase(emailAddressOfTheAccountIsConfirmed: false);

        await TryWrongPasswordThisNumberOfTimesAsync(6);

        Assert.Empty(queueThatKeepsTheEmailRequestsInMemory.RequestsAddedToTheBackgroundQueue);
    }

    // --- Appareils

    [Fact]
    public async Task FirstLoginRegistersTheDeviceWithOnlyTheHashOfItsToken()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase();

        var resultOfTheLoginOfTheAccount = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, PasswordOfTheAccountInTheseTests);

        var deviceSavedInMemoryForTheAccount = Assert.Single(repositoryThatKeepsSessionsInMemory.SavedDevices);
        Assert.Equal(accountBeingTestedInThisLogin.Id, deviceSavedInMemoryForTheAccount.UserId);
        Assert.Equal(realGeneratorOfTheTokensInTheseTests.ConvertSecureTokenIntoItsHash(resultOfTheLoginOfTheAccount.DeviceTokenToPutInTheCookie!), deviceSavedInMemoryForTheAccount.DeviceToken);
        Assert.NotEqual(resultOfTheLoginOfTheAccount.DeviceTokenToPutInTheCookie, deviceSavedInMemoryForTheAccount.DeviceToken);
        Assert.NotEqual(resultOfTheLoginOfTheAccount.SessionTokenToPutInTheCookie, resultOfTheLoginOfTheAccount.DeviceTokenToPutInTheCookie);
    }

    // Fait une connexion réussie avec ces cookies déjà présents dans le navigateur.
    private async Task<AccountLoginResult> LogInWithTheseCookiesAlreadyInTheBrowserAsync(string? deviceTokenAlreadyInTheBrowser, string? sessionTokenAlreadyInTheBrowser)
    {
        return await CreateLoginServiceWithFakeTools().LogVisitorInWithEmailAddressAndPasswordAsync(new LoginAttemptFromVisitor
        {
            EmailAddressTypedByTheVisitor = EmailAddressOfTheAccountInTheseTests,
            PasswordTextTypedByTheVisitor = PasswordOfTheAccountInTheseTests,
            IpAddressOfTheVisitor = IpAddressOfTheVisitorInTheseTests,
            DeviceTokenReadInTheCookie = deviceTokenAlreadyInTheBrowser,
            SessionTokenReadInTheCookie = sessionTokenAlreadyInTheBrowser
        });
    }

    [Fact]
    public async Task KnownDeviceKeepsItsTokenAndAddsNothing()
    {
        AddAccountToTheFakeDatabase();
        var resultOfTheFirstLoginOfTheAccount = await LogInWithTheseCookiesAlreadyInTheBrowserAsync(null, null);

        var resultOfTheSecondLoginOfTheAccount = await LogInWithTheseCookiesAlreadyInTheBrowserAsync(resultOfTheFirstLoginOfTheAccount.DeviceTokenToPutInTheCookie, null);

        Assert.Equal(resultOfTheFirstLoginOfTheAccount.DeviceTokenToPutInTheCookie, resultOfTheSecondLoginOfTheAccount.DeviceTokenToPutInTheCookie);
        Assert.Single(repositoryThatKeepsSessionsInMemory.SavedDevices);
    }

    [Fact]
    public async Task UnknownDeviceTokenIsReplacedByANewOne()
    {
        AddAccountToTheFakeDatabase();

        var resultOfTheLoginOfTheAccount = await LogInWithTheseCookiesAlreadyInTheBrowserAsync("jeton-d-appareil-invente", null);

        Assert.NotEqual("jeton-d-appareil-invente", resultOfTheLoginOfTheAccount.DeviceTokenToPutInTheCookie);
        Assert.Single(repositoryThatKeepsSessionsInMemory.SavedDevices);
    }

    // Le jeton d'un autre compte ne rend pas l'appareil connu pour celui-ci.
    [Fact]
    public async Task DeviceTokenOfAnotherAccountIsReplacedByANewOne()
    {
        AddAccountToTheFakeDatabase();
        var deviceTokenOfTheOtherAccount = "jeton-d-appareil-d-un-autre-compte";
        repositoryThatKeepsSessionsInMemory.SavedDevices.Add(new Device
        {
            DeviceToken = realGeneratorOfTheTokensInTheseTests.ConvertSecureTokenIntoItsHash(deviceTokenOfTheOtherAccount),
            UserId = Guid.NewGuid()
        });

        var resultOfTheLoginOfTheAccount = await LogInWithTheseCookiesAlreadyInTheBrowserAsync(deviceTokenOfTheOtherAccount, null);

        Assert.NotEqual(deviceTokenOfTheOtherAccount, resultOfTheLoginOfTheAccount.DeviceTokenToPutInTheCookie);
        Assert.Equal(2, repositoryThatKeepsSessionsInMemory.SavedDevices.Count);
    }

    // --- Ancienne session du navigateur

    [Fact]
    public async Task SuccessfulLoginClosesTheOldSessionOfThisBrowser()
    {
        AddAccountToTheFakeDatabase();
        var resultOfTheFirstLoginOfTheAccount = await LogInWithTheseCookiesAlreadyInTheBrowserAsync(null, null);

        var resultOfTheSecondLoginOfTheAccount = await LogInWithTheseCookiesAlreadyInTheBrowserAsync(null, resultOfTheFirstLoginOfTheAccount.SessionTokenToPutInTheCookie);

        var sessionStillOpenAfterTheSecondLogin = Assert.Single(repositoryThatKeepsSessionsInMemory.SavedSessions);
        Assert.Equal(realGeneratorOfTheTokensInTheseTests.ConvertSecureTokenIntoItsHash(resultOfTheSecondLoginOfTheAccount.SessionTokenToPutInTheCookie!), sessionStillOpenAfterTheSecondLogin.Id);
    }

    // Un mauvais mot de passe ne doit pas déconnecter le navigateur.
    [Fact]
    public async Task FailedLoginKeepsTheOldSessionOfThisBrowser()
    {
        AddAccountToTheFakeDatabase();
        var resultOfTheFirstLoginOfTheAccount = await LogInWithTheseCookiesAlreadyInTheBrowserAsync(null, null);

        await CreateLoginServiceWithFakeTools().LogVisitorInWithEmailAddressAndPasswordAsync(new LoginAttemptFromVisitor
        {
            EmailAddressTypedByTheVisitor = EmailAddressOfTheAccountInTheseTests,
            PasswordTextTypedByTheVisitor = WrongPasswordTypedInTheseTests,
            SessionTokenReadInTheCookie = resultOfTheFirstLoginOfTheAccount.SessionTokenToPutInTheCookie
        });

        Assert.Single(repositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    // --- Normalisation

    [Fact]
    public async Task PasswordTypedWithDecomposedAccentMatchesThePasswordChosenWithComposedAccent()
    {
        var accountBeingTestedInThisLogin = AddAccountToTheFakeDatabase();
        accountBeingTestedInThisLogin.PasswordHash = FakePasswordHasher.PrefixOfTheFakePasswordHashes + "Mot-De-Passé-1";

        var resultOfTheLoginOfTheAccount = await TryToLogInWithThisEmailAddressAndPasswordAsync(EmailAddressOfTheAccountInTheseTests, "Mot-De-Passé-1");

        Assert.True(resultOfTheLoginOfTheAccount.IsLoggedIn);
    }

    // Ces caractères font lever une erreur à .NET : le visiteur doit recevoir le refus normal, pas une erreur 500.
    [Theory]
    [InlineData(0xFFFE)]
    [InlineData(0xD800)]
    public async Task PasswordThatCannotBeNormalizedIsRefusedWithTheGenericMessage(int codeOfTheCharacterThatCannotBeNormalized)
    {
        AddAccountToTheFakeDatabase();

        var resultOfTheLoginWithHostilePassword = await TryToLogInWithThisEmailAddressAndPasswordAsync(
            EmailAddressOfTheAccountInTheseTests,
            PasswordOfTheAccountInTheseTests + (char)codeOfTheCharacterThatCannotBeNormalized);

        CheckThatLoginWasRefusedWithTheGenericMessage(resultOfTheLoginWithHostilePassword);
    }
}
