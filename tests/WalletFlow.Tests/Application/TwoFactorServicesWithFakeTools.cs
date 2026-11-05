using System.Net;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Prépare les services de la double authentification pour les tests : la base, le hachage et l'horloge sont faux.
// Le calcul des codes, le chiffrement et les jetons sont les vrais : ces tests vérifient de vrais codes.
public class TwoFactorServicesWithFakeTools
{
    public const string EmailAddressOfTheAccountInTheseTests = "utilisateur@exemple.test";
    public const string PasswordOfTheAccountInTheseTests = "Mot-De-Passe-Valide-1";
    public const string WrongPasswordTypedInTheseTests = "Mauvais-Mot-De-Passe-2";

    public static readonly IPAddress IpAddressOfTheVisitorInTheseTests = IPAddress.Parse("203.0.113.7");

    public FakeUserAccountRepository RepositoryThatKeepsAccountsInMemory { get; } = new FakeUserAccountRepository();
    public FakeSecurityEventRepository RepositoryThatKeepsSecurityEventsInMemory { get; } = new FakeSecurityEventRepository();
    public FakeUserSessionRepository RepositoryThatKeepsSessionsInMemory { get; }
    public FakeTwoFactorAuthenticationRepository RepositoryThatKeepsTwoFactorDataInMemory { get; }
    public FakeBackgroundEmailRequestQueue QueueThatKeepsTheEmailRequestsInMemory { get; } = new FakeBackgroundEmailRequestQueue();
    public FakePasswordHasher HasherThatCountsTheComparedPasswords { get; } = new FakePasswordHasher();
    public FakeTimeProviderForTests ClockThatOnlyMovesWhenTestsAskIt { get; } = new FakeTimeProviderForTests();
    public RandomSecureTokenGenerator RealGeneratorOfTheTokens { get; } = new RandomSecureTokenGenerator();
    public OtpNetAuthenticatorCodeTool RealToolThatCalculatesTheCodes { get; } = new OtpNetAuthenticatorCodeTool();
    public AesGcmTotpSecretProtector RealProtectorOfTheSecrets { get; set; } = new AesGcmTotpSecretProtector(TwoFactorTestTools.CreateNewEncryptionKeyForOneTest());
    public InMemoryLoginsWaitingForTheirCodeStore StoreOfTheLoginsWaitingForTheirCode { get; }
    public InMemorySecretsWaitingForActivationStore StoreOfTheSecretsWaitingForActivation { get; }
    public InMemoryUsedAuthenticatorCodesRegister RegisterOfTheCodesAlreadyUsed { get; }

    // Relie les faux accès entre eux, et donne aux outils en mémoire la fausse horloge.
    public TwoFactorServicesWithFakeTools()
    {
        RepositoryThatKeepsSessionsInMemory = new FakeUserSessionRepository(RepositoryThatKeepsSecurityEventsInMemory);
        RepositoryThatKeepsTwoFactorDataInMemory = new FakeTwoFactorAuthenticationRepository(RepositoryThatKeepsAccountsInMemory, RepositoryThatKeepsSecurityEventsInMemory);
        StoreOfTheLoginsWaitingForTheirCode = new InMemoryLoginsWaitingForTheirCodeStore(ClockThatOnlyMovesWhenTestsAskIt);
        StoreOfTheSecretsWaitingForActivation = new InMemorySecretsWaitingForActivationStore(ClockThatOnlyMovesWhenTestsAskIt);
        RegisterOfTheCodesAlreadyUsed = new InMemoryUsedAuthenticatorCodesRegister(ClockThatOnlyMovesWhenTestsAskIt);
    }

    // Crée le service qui vérifie les codes.
    public TwoFactorCodeCheckingService CreateTwoFactorCodeCheckingServiceWithFakeTools()
    {
        return new TwoFactorCodeCheckingService(
            RepositoryThatKeepsTwoFactorDataInMemory,
            HasherThatCountsTheComparedPasswords,
            RealToolThatCalculatesTheCodes,
            RealProtectorOfTheSecrets,
            RegisterOfTheCodesAlreadyUsed,
            ClockThatOnlyMovesWhenTestsAskIt);
    }

    // Crée le service qui ouvre les sessions.
    public UserSessionOpeningService CreateSessionOpeningServiceWithFakeTools()
    {
        return new UserSessionOpeningService(
            RepositoryThatKeepsAccountsInMemory,
            RepositoryThatKeepsSessionsInMemory,
            RepositoryThatKeepsTwoFactorDataInMemory,
            RealGeneratorOfTheTokens,
            ClockThatOnlyMovesWhenTestsAskIt);
    }

    // Crée le service qui vérifie le mot de passe.
    public AccountLoginService CreateAccountLoginServiceWithFakeTools()
    {
        return new AccountLoginService(
            RepositoryThatKeepsAccountsInMemory,
            RepositoryThatKeepsSecurityEventsInMemory,
            QueueThatKeepsTheEmailRequestsInMemory,
            HasherThatCountsTheComparedPasswords,
            RealGeneratorOfTheTokens,
            StoreOfTheLoginsWaitingForTheirCode,
            CreateSessionOpeningServiceWithFakeTools(),
            ClockThatOnlyMovesWhenTestsAskIt);
    }

    // Crée le service qui finit la connexion avec un code.
    public TwoFactorLoginService CreateTwoFactorLoginServiceWithFakeTools()
    {
        return new TwoFactorLoginService(
            RepositoryThatKeepsAccountsInMemory,
            RepositoryThatKeepsSecurityEventsInMemory,
            RealGeneratorOfTheTokens,
            StoreOfTheLoginsWaitingForTheirCode,
            CreateTwoFactorCodeCheckingServiceWithFakeTools(),
            CreateSessionOpeningServiceWithFakeTools(),
            ClockThatOnlyMovesWhenTestsAskIt);
    }

    // Crée le service qui active la double authentification.
    public TwoFactorActivationService CreateTwoFactorActivationServiceWithFakeTools()
    {
        return new TwoFactorActivationService(
            RepositoryThatKeepsAccountsInMemory,
            RepositoryThatKeepsTwoFactorDataInMemory,
            RepositoryThatKeepsSecurityEventsInMemory,
            HasherThatCountsTheComparedPasswords,
            RealGeneratorOfTheTokens,
            RealToolThatCalculatesTheCodes,
            RealProtectorOfTheSecrets,
            StoreOfTheSecretsWaitingForActivation,
            CreateTwoFactorCodeCheckingServiceWithFakeTools(),
            ClockThatOnlyMovesWhenTestsAskIt);
    }

    // Crée le service qui désactive la double authentification.
    public TwoFactorDeactivationService CreateTwoFactorDeactivationServiceWithFakeTools()
    {
        return new TwoFactorDeactivationService(
            RepositoryThatKeepsAccountsInMemory,
            RepositoryThatKeepsTwoFactorDataInMemory,
            RepositoryThatKeepsSecurityEventsInMemory,
            HasherThatCountsTheComparedPasswords,
            CreateTwoFactorCodeCheckingServiceWithFakeTools(),
            ClockThatOnlyMovesWhenTestsAskIt);
    }

    // Crée le service qui change un mot de passe oublié.
    public PasswordResetService CreatePasswordResetServiceWithFakeTools()
    {
        return new PasswordResetService(
            RepositoryThatKeepsAccountsInMemory,
            RepositoryThatKeepsSessionsInMemory,
            HasherThatCountsTheComparedPasswords,
            RealGeneratorOfTheTokens,
            ClockThatOnlyMovesWhenTestsAskIt);
    }

    // Ajoute à la fausse base un compte confirmé, sans double authentification.
    public User AddAccountWithoutTwoFactorToTheFakeDatabase(string emailAddressOfTheAccountToAdd = EmailAddressOfTheAccountInTheseTests)
    {
        var accountAddedToTheFakeDatabase = new User
        {
            Email = emailAddressOfTheAccountToAdd,
            PasswordHash = FakePasswordHasher.PrefixOfTheFakePasswordHashes + PasswordOfTheAccountInTheseTests,
            IsEmailConfirmed = true
        };
        RepositoryThatKeepsAccountsInMemory.SavedUsers.Add(accountAddedToTheFakeDatabase);

        return accountAddedToTheFakeDatabase;
    }

    // Ajoute à la fausse base un compte dont la double authentification est déjà activée, avec son secret et ses 10 codes de secours.
    public ActivatedAccountOfTheseTests AddActivatedAccountToTheFakeDatabase(string emailAddressOfTheAccountToAdd = EmailAddressOfTheAccountInTheseTests)
    {
        var accountAddedToTheFakeDatabase = AddAccountWithoutTwoFactorToTheFakeDatabase(emailAddressOfTheAccountToAdd);
        var secretSharedWithTheAuthenticatorApplication = RealToolThatCalculatesTheCodes.GenerateNewRandomSecretForTheAuthenticatorApplication();
        accountAddedToTheFakeDatabase.IsTwoFactorEnabled = true;
        accountAddedToTheFakeDatabase.EncryptedTotpSecret = RealProtectorOfTheSecrets.EncryptSecretOfThisUserAccount(secretSharedWithTheAuthenticatorApplication, accountAddedToTheFakeDatabase.Id);

        var backupCodesInTheirCleanForm = new List<string>();
        for (var numberOfTheBackupCodeBeingAdded = 1; numberOfTheBackupCodeBeingAdded <= TwoFactorAuthenticationRules.NumberOfBackupCodesGivenAtEachActivation; numberOfTheBackupCodeBeingAdded++)
        {
            var backupCodeInItsCleanForm = RealGeneratorOfTheTokens.GenerateNewRandomBackupCodeInItsCleanForm();
            backupCodesInTheirCleanForm.Add(backupCodeInItsCleanForm);
            RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes.Add(new BackupCode
            {
                CodeHash = FakePasswordHasher.PrefixOfTheFakePasswordHashes + backupCodeInItsCleanForm,
                UserId = accountAddedToTheFakeDatabase.Id
            });
        }

        return new ActivatedAccountOfTheseTests
        {
            UserAccount = accountAddedToTheFakeDatabase,
            SecretOfTheAuthenticatorApplication = secretSharedWithTheAuthenticatorApplication,
            BackupCodesInTheirCleanForm = backupCodesInTheirCleanForm
        };
    }

    // Calcule le code qu'affiche le téléphone à la date de la fausse horloge.
    public string CalculateCodeShownOnThePhoneNow(string secretSharedWithTheAuthenticatorApplication)
    {
        return TwoFactorTestTools.CalculateAuthenticatorCodeShownAtThisDate(secretSharedWithTheAuthenticatorApplication, ClockThatOnlyMovesWhenTestsAskIt.GetUtcNow());
    }

    // Donne un code à 6 chiffres qui ne vaut rien à la date de la fausse horloge.
    public string FindCodeThatIsWrongNow(string secretSharedWithTheAuthenticatorApplication)
    {
        return TwoFactorTestTools.FindAuthenticatorCodeThatIsWrongAtThisDate(secretSharedWithTheAuthenticatorApplication, ClockThatOnlyMovesWhenTestsAskIt.GetUtcNow());
    }

    // Passe à la période suivante : le téléphone affiche un nouveau code.
    public void WaitForTheNextCodeOnThePhone()
    {
        ClockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromSeconds(TwoFactorTestTools.NumberOfSecondsInOneAuthenticatorPeriod));
    }

    // Fait une tentative de connexion avec ce mot de passe.
    public async Task<AccountLoginResult> TryToLogInWithThisPasswordAsync(string passwordTypedByTheVisitorInThisTest)
    {
        return await CreateAccountLoginServiceWithFakeTools().LogVisitorInWithEmailAddressAndPasswordAsync(new LoginAttemptFromVisitor
        {
            EmailAddressTypedByTheVisitor = EmailAddressOfTheAccountInTheseTests,
            PasswordTextTypedByTheVisitor = passwordTypedByTheVisitorInThisTest,
            IpAddressOfTheVisitor = IpAddressOfTheVisitorInTheseTests
        });
    }

    // Donne le bon mot de passe, et renvoie le jeton de la connexion qui attend son code.
    public async Task<string> LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync()
    {
        var resultOfTheLoginWithTheRightPassword = await TryToLogInWithThisPasswordAsync(PasswordOfTheAccountInTheseTests);

        return resultOfTheLoginWithTheRightPassword.WaitingLoginTokenToPutInTheCookie!;
    }

    // Finit la connexion avec ce code.
    public async Task<TwoFactorLoginResult> FinishLoginWithThisCodeAsync(string? waitingLoginTokenReadInTheCookie, string codeTypedByTheVisitorInThisTest)
    {
        return await CreateTwoFactorLoginServiceWithFakeTools().FinishLoginOfTheVisitorWithThisCodeAsync(new TwoFactorCodeAttemptFromVisitor
        {
            CodeTypedByTheVisitor = codeTypedByTheVisitorInThisTest,
            IpAddressOfTheVisitor = IpAddressOfTheVisitorInTheseTests,
            WaitingLoginTokenReadInTheCookie = waitingLoginTokenReadInTheCookie
        });
    }

    // Compte les événements de ce type et de ce résultat dans le faux journal.
    public int CountSecurityEventsInTheJournal(SecurityEventType typeOfTheEventsToCount, SecurityEventResult resultOfTheEventsToCount)
    {
        var numberOfEventsFoundInTheJournal = 0;

        foreach (var securityEventKeptInTheJournal in RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents)
        {
            if (securityEventKeptInTheJournal.Type == typeOfTheEventsToCount && securityEventKeptInTheJournal.Result == resultOfTheEventsToCount)
            {
                numberOfEventsFoundInTheJournal = numberOfEventsFoundInTheJournal + 1;
            }
        }

        return numberOfEventsFoundInTheJournal;
    }
}

// Un compte activé dans la fausse base, avec ce que son propriétaire connaît : le secret de son téléphone et ses codes de secours.
public class ActivatedAccountOfTheseTests
{
    public required User UserAccount { get; init; }
    public required string SecretOfTheAuthenticatorApplication { get; init; }
    public required List<string> BackupCodesInTheirCleanForm { get; init; }
}
