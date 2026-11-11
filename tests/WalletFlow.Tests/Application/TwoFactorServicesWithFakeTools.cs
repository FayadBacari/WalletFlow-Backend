using System.Net;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserSessions;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Prépare les services de la double authentification pour les tests : la base, le hachage et l'horloge sont faux.
// Le calcul des codes, le chiffrement et les jetons sont les vrais : ces tests vérifient de vrais codes.
public class TwoFactorServicesWithFakeTools
{
    public const string AccountEmail = "utilisateur@exemple.test";
    public const string AccountPassword = "Mot-De-Passe-Valide-1";
    public const string WrongPassword = "Mauvais-Mot-De-Passe-2";

    public static readonly IPAddress IpAddress = IPAddress.Parse("203.0.113.7");

    public FakeUserAccountRepository AccountRepository { get; } = new FakeUserAccountRepository();
    public FakeSecurityEventRepository SecurityEventRepository { get; } = new FakeSecurityEventRepository();
    public FakeUserSessionRepository SessionRepository { get; } = new FakeUserSessionRepository();
    public FakeTwoFactorAuthenticationRepository TwoFactorRepository { get; } = new FakeTwoFactorAuthenticationRepository();
    public FakeDatabaseUnitOfWork UnitOfWork { get; }
    public FakeBackgroundEmailRequestQueue EmailQueue { get; } = new FakeBackgroundEmailRequestQueue();
    public FakePasswordHasher PasswordHasher { get; } = new FakePasswordHasher();
    public FakeBackupCodeHasher BackupCodeHasher { get; } = new FakeBackupCodeHasher();
    public FakeTimeProvider Clock { get; } = new FakeTimeProvider();
    public RandomSecureTokenGenerator TokenGenerator { get; } = new RandomSecureTokenGenerator();
    public OtpNetAuthenticatorCodeTool AuthenticatorCodeTool { get; } = new OtpNetAuthenticatorCodeTool();
    public AesGcmTotpSecretProtector SecretProtector { get; set; } = new AesGcmTotpSecretProtector(TwoFactorTestTools.CreateEncryptionKey());
    public InMemoryWaitingLoginStore WaitingLoginStore { get; }
    public InMemoryWaitingSecretStore WaitingSecretStore { get; }
    public InMemoryUsedAuthenticatorCodesRegister UsedCodesRegister { get; }

    // Relie les faux accès à la fausse unité de travail, et donne aux outils en mémoire la fausse horloge.
    public TwoFactorServicesWithFakeTools()
    {
        UnitOfWork = new FakeDatabaseUnitOfWork(
            AccountRepository,
            SessionRepository,
            TwoFactorRepository,
            SecurityEventRepository);
        WaitingLoginStore = new InMemoryWaitingLoginStore(Clock);
        WaitingSecretStore = new InMemoryWaitingSecretStore(Clock);
        UsedCodesRegister = new InMemoryUsedAuthenticatorCodesRegister(Clock);
    }

    // Crée le service qui enregistre les changements, avec ses réessais.
    public DatabaseChangesSavingService CreateSavingService()
    {
        return new DatabaseChangesSavingService(UnitOfWork);
    }

    // Crée le service qui écrit dans le faux journal de sécurité.
    public SecurityJournalService CreateSecurityJournal()
    {
        return new SecurityJournalService(SecurityEventRepository, Clock);
    }

    // Crée le service qui vérifie les codes.
    public TwoFactorCodeCheckingService CreateCodeCheckingService()
    {
        return new TwoFactorCodeCheckingService(
            TwoFactorRepository,
            BackupCodeHasher,
            AuthenticatorCodeTool,
            SecretProtector,
            UsedCodesRegister,
            Clock);
    }

    // Crée le service qui ouvre les sessions.
    public UserSessionOpeningService CreateSessionOpeningService()
    {
        return new UserSessionOpeningService(
            AccountRepository,
            SessionRepository,
            TwoFactorRepository,
            CreateSavingService(),
            CreateSecurityJournal(),
            TokenGenerator,
            Clock);
    }

    // Crée le service qui vérifie le mot de passe.
    public AccountLoginService CreateLoginService()
    {
        return new AccountLoginService(
            AccountRepository,
            CreateSavingService(),
            CreateSecurityJournal(),
            EmailQueue,
            PasswordHasher,
            TokenGenerator,
            WaitingLoginStore,
            CreateSessionOpeningService(),
            Clock);
    }

    // Crée le service qui finit la connexion avec un code.
    public TwoFactorLoginService CreateTwoFactorLoginService()
    {
        return new TwoFactorLoginService(
            AccountRepository,
            CreateSecurityJournal(),
            TokenGenerator,
            WaitingLoginStore,
            CreateCodeCheckingService(),
            CreateSessionOpeningService());
    }

    // Crée le service qui active la double authentification.
    public TwoFactorActivationService CreateActivationService()
    {
        return new TwoFactorActivationService(
            AccountRepository,
            TwoFactorRepository,
            CreateSavingService(),
            CreateSecurityJournal(),
            PasswordHasher,
            BackupCodeHasher,
            TokenGenerator,
            AuthenticatorCodeTool,
            SecretProtector,
            WaitingSecretStore,
            CreateCodeCheckingService());
    }

    // Crée le service qui désactive la double authentification.
    public TwoFactorDeactivationService CreateDeactivationService()
    {
        return new TwoFactorDeactivationService(
            AccountRepository,
            TwoFactorRepository,
            CreateSavingService(),
            CreateSecurityJournal(),
            PasswordHasher,
            CreateCodeCheckingService());
    }

    // Crée le service qui change un mot de passe oublié.
    public PasswordResetService CreatePasswordResetService()
    {
        return new PasswordResetService(
            AccountRepository,
            SessionRepository,
            CreateSavingService(),
            CreateSecurityJournal(),
            PasswordHasher,
            TokenGenerator,
            Clock);
    }

    // Ajoute à la fausse base un compte confirmé, sans double authentification.
    public User AddAccountWithoutTwoFactor(string emailAddress = AccountEmail, short lockoutCount = 0)
    {
        var account = new User
        {
            Email = emailAddress,
            PasswordHash = FakePasswordHasher.HashPrefix + AccountPassword,
            IsEmailConfirmed = true,
            LockoutCount = lockoutCount
        };
        AccountRepository.SavedUsers.Add(account);

        return account;
    }

    // Ajoute à la fausse base un compte dont la double authentification est déjà activée, avec son secret et ses 10 codes de secours.
    public ActivatedAccount AddActivatedAccount(string emailAddress = AccountEmail, short lockoutCount = 0)
    {
        var account = AddAccountWithoutTwoFactor(emailAddress, lockoutCount);
        var secret = AuthenticatorCodeTool.GenerateSecret();
        account.ActivateTwoFactor(SecretProtector.Encrypt(secret, account.Id));

        var cleanedCodes = new List<string>();
        for (var codeNumber = 1; codeNumber <= TwoFactorAuthenticationRules.BackupCodesPerActivation; codeNumber++)
        {
            var cleanedCode = TokenGenerator.GenerateBackupCode();
            cleanedCodes.Add(cleanedCode);
            TwoFactorRepository.SavedBackupCodes.Add(new BackupCode
            {
                CodeHash = FakeBackupCodeHasher.CreateFakeHash(cleanedCode, account.Id),
                UserId = account.Id
            });
        }

        return new ActivatedAccount
        {
            UserAccount = account,
            Secret = secret,
            BackupCodes = cleanedCodes
        };
    }

    // Calcule le code qu'affiche le téléphone à la date de la fausse horloge.
    public string CalculateCurrentCode(string secret)
    {
        return TwoFactorTestTools.CalculateCodeAt(secret, Clock.GetUtcNow());
    }

    // Donne un code à 6 chiffres qui ne vaut rien à la date de la fausse horloge.
    public string FindWrongCode(string secret)
    {
        return TwoFactorTestTools.FindWrongCodeAt(secret, Clock.GetUtcNow());
    }

    // Passe à la période suivante : le téléphone affiche un nouveau code.
    public void WaitForNextCode()
    {
        Clock.Advance(TimeSpan.FromSeconds(TwoFactorTestTools.PeriodInSeconds));
    }

    // Fait une tentative de connexion avec ce mot de passe.
    public async Task<AccountLoginResult> LogInAsync(string password)
    {
        return await CreateLoginService().LogInAsync(new LoginAttempt
        {
            EmailAddress = AccountEmail,
            Password = password,
            IpAddress = IpAddress
        });
    }

    // Donne le bon mot de passe, et renvoie le jeton de la connexion qui attend son code.
    public async Task<string> LogInThenGetWaitingLoginTokenAsync()
    {
        var result = await LogInAsync(AccountPassword);

        return Assert.IsType<AccountLoginResult.WaitingForTwoFactorCode>(result).WaitingLoginToken;
    }

    // Finit la connexion avec ce code.
    public async Task<TwoFactorLoginResult> FinishLoginAsync(string? waitingLoginToken, string code)
    {
        return await CreateTwoFactorLoginService().FinishAsync(new TwoFactorCodeAttempt
        {
            Code = code,
            IpAddress = IpAddress,
            WaitingLoginToken = waitingLoginToken
        });
    }

    // Compte les événements de ce type et de ce résultat dans le faux journal.
    public int CountSecurityEvents(SecurityEventType type, SecurityEventResult result)
    {
        var eventCount = 0;

        foreach (var securityEvent in SecurityEventRepository.SavedSecurityEvents)
        {
            if (securityEvent.Type == type && securityEvent.Result == result)
            {
                eventCount = eventCount + 1;
            }
        }

        return eventCount;
    }
}

// Un compte activé dans la fausse base, avec ce que son propriétaire connaît : le secret de son téléphone et ses codes de secours.
public class ActivatedAccount
{
    public required User UserAccount { get; init; }
    public required string Secret { get; init; }
    public required List<string> BackupCodes { get; init; }
}
