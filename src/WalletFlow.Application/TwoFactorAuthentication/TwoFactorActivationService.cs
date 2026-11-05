using System.Net;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.TwoFactorAuthentication;

// Cas d'utilisation : un utilisateur connecté active la double authentification, en deux temps (GR Auth, SS §2).
// Le mot de passe donne le secret, puis un premier code active tout : un code QR mal scanné n'enferme donc personne dehors.
public class TwoFactorActivationService
{
    public const string ErrorMessageForWrongPasswordAtActivation = "Ce mot de passe n'est pas le bon.";
    public const string ErrorMessageForTwoFactorAlreadyEnabled = "La double authentification est déjà activée.";
    public const string ErrorMessageForActivationThatMustRestart = "Cette activation n'est plus valable. Recommence depuis le début.";

    private readonly IUserAccountRepository repositoryOfTheUserAccountsInDatabase;
    private readonly ITwoFactorAuthenticationRepository repositoryOfTheTwoFactorDataInDatabase;
    private readonly ISecurityEventRepository repositoryOfTheSecurityEventsInDatabase;
    private readonly IPasswordHasher hasherOfThePasswordsAndBackupCodes;
    private readonly ISecureTokenGenerator generatorOfTheSecureRandomTokens;
    private readonly IAuthenticatorCodeTool toolThatCalculatesTheAuthenticatorCodes;
    private readonly ITotpSecretProtector protectorOfTheSecretsInTheDatabase;
    private readonly ISecretsWaitingForActivationStore storeOfTheSecretsWaitingForActivation;
    private readonly TwoFactorCodeCheckingService serviceThatChecksTheTwoFactorCodes;
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;

    // Reçoit les outils dont l'activation a besoin.
    public TwoFactorActivationService(
        IUserAccountRepository repositoryOfTheUserAccountsInDatabase,
        ITwoFactorAuthenticationRepository repositoryOfTheTwoFactorDataInDatabase,
        ISecurityEventRepository repositoryOfTheSecurityEventsInDatabase,
        IPasswordHasher hasherOfThePasswordsAndBackupCodes,
        ISecureTokenGenerator generatorOfTheSecureRandomTokens,
        IAuthenticatorCodeTool toolThatCalculatesTheAuthenticatorCodes,
        ITotpSecretProtector protectorOfTheSecretsInTheDatabase,
        ISecretsWaitingForActivationStore storeOfTheSecretsWaitingForActivation,
        TwoFactorCodeCheckingService serviceThatChecksTheTwoFactorCodes,
        TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.repositoryOfTheUserAccountsInDatabase = repositoryOfTheUserAccountsInDatabase;
        this.repositoryOfTheTwoFactorDataInDatabase = repositoryOfTheTwoFactorDataInDatabase;
        this.repositoryOfTheSecurityEventsInDatabase = repositoryOfTheSecurityEventsInDatabase;
        this.hasherOfThePasswordsAndBackupCodes = hasherOfThePasswordsAndBackupCodes;
        this.generatorOfTheSecureRandomTokens = generatorOfTheSecureRandomTokens;
        this.toolThatCalculatesTheAuthenticatorCodes = toolThatCalculatesTheAuthenticatorCodes;
        this.protectorOfTheSecretsInTheDatabase = protectorOfTheSecretsInTheDatabase;
        this.storeOfTheSecretsWaitingForActivation = storeOfTheSecretsWaitingForActivation;
        this.serviceThatChecksTheTwoFactorCodes = serviceThatChecksTheTwoFactorCodes;
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Premier temps : le mot de passe redemandé donne le secret. Une session volée ne peut donc pas enfermer le propriétaire dehors (SS §2).
    // Rien n'est écrit en base : le secret attend le premier code, en mémoire.
    public async Task<TwoFactorActivationStartResult> StartActivationWithThePasswordOfTheUserAsync(Guid identifierOfTheLoggedInUser, string passwordTypedByTheLoggedInUser, IPAddress? ipAddressOfTheLoggedInUser)
    {
        var accountOfTheLoggedInUser = await repositoryOfTheUserAccountsInDatabase.FindUserAccountByIdentifierAsync(identifierOfTheLoggedInUser);

        if (accountOfTheLoggedInUser == null)
        {
            return new TwoFactorActivationStartResult { WrongPasswordErrorMessage = ErrorMessageForWrongPasswordAtActivation };
        }

        if (accountOfTheLoggedInUser.IsTwoFactorEnabled)
        {
            return new TwoFactorActivationStartResult { AlreadyEnabledErrorMessage = ErrorMessageForTwoFactorAlreadyEnabled };
        }

        var passwordWrittenInItsNormalizedForm = PasswordRules.ConvertPasswordIntoItsNormalizedForm(passwordTypedByTheLoggedInUser);

        if (!hasherOfThePasswordsAndBackupCodes.CheckThatPasswordMatchesItsSecureHash(passwordWrittenInItsNormalizedForm, accountOfTheLoggedInUser.PasswordHash))
        {
            await SaveSecurityEventOfThisActivationAttemptAsync(SecurityEventResult.Failure, identifierOfTheLoggedInUser, ipAddressOfTheLoggedInUser);
            return new TwoFactorActivationStartResult { WrongPasswordErrorMessage = ErrorMessageForWrongPasswordAtActivation };
        }

        var newSecretOfTheAuthenticatorApplication = toolThatCalculatesTheAuthenticatorCodes.GenerateNewRandomSecretForTheAuthenticatorApplication();
        storeOfTheSecretsWaitingForActivation.SaveSecretWaitingForActivationOfThisAccount(identifierOfTheLoggedInUser, newSecretOfTheAuthenticatorApplication);

        return new TwoFactorActivationStartResult
        {
            SecretOfTheAuthenticatorApplication = newSecretOfTheAuthenticatorApplication,
            LinkThatAddsTheSecretToTheApplication = toolThatCalculatesTheAuthenticatorCodes.BuildLinkThatAddsThisSecretToTheApplication(newSecretOfTheAuthenticatorApplication, accountOfTheLoggedInUser.Email)
        };
    }

    // Second temps : un premier code juste active tout, et les 10 codes de secours sont donnés une seule fois (GR Auth).
    public async Task<TwoFactorActivationFinishResult> FinishActivationWithTheFirstCodeOfTheUserAsync(Guid identifierOfTheLoggedInUser, string codeTypedByTheLoggedInUser, IPAddress? ipAddressOfTheLoggedInUser)
    {
        var secretWaitingForTheFirstCode = storeOfTheSecretsWaitingForActivation.FindSecretWaitingForActivationOfThisAccount(identifierOfTheLoggedInUser);

        if (secretWaitingForTheFirstCode == null)
        {
            return new TwoFactorActivationFinishResult { ActivationMustRestartErrorMessage = ErrorMessageForActivationThatMustRestart };
        }

        if (!serviceThatChecksTheTwoFactorCodes.CheckThatFirstCodeMatchesTheSecretWaitingForActivation(identifierOfTheLoggedInUser, secretWaitingForTheFirstCode, codeTypedByTheLoggedInUser))
        {
            await SaveSecurityEventOfThisActivationAttemptAsync(SecurityEventResult.Failure, identifierOfTheLoggedInUser, ipAddressOfTheLoggedInUser);
            return new TwoFactorActivationFinishResult { WrongCodeErrorMessage = TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode };
        }

        return await SaveActivationThenGiveTheBackupCodesAsync(identifierOfTheLoggedInUser, secretWaitingForTheFirstCode, ipAddressOfTheLoggedInUser);
    }

    // Prépare les codes de secours et le secret chiffré, enregistre le tout, puis oublie le secret en mémoire.
    // Le hachage est lent : il passe avant la lecture du compte, pour que l'écriture suive de près la lecture.
    private async Task<TwoFactorActivationFinishResult> SaveActivationThenGiveTheBackupCodesAsync(Guid identifierOfTheLoggedInUser, string secretWaitingForTheFirstCode, IPAddress? ipAddressOfTheLoggedInUser)
    {
        var backupCodesInTheirCleanForm = GenerateNewBackupCodesInTheirCleanForm();
        var backupCodesToSaveInTheDatabase = HashBackupCodesForThisAccount(backupCodesInTheirCleanForm, identifierOfTheLoggedInUser);
        var secretOfThePhoneEncryptedForTheDatabase = protectorOfTheSecretsInTheDatabase.EncryptSecretOfThisUserAccount(secretWaitingForTheFirstCode, identifierOfTheLoggedInUser);

        var activationWasSavedInTheDatabase = await TryToSaveActivationUnlessOtherRequestsKeepChangingTheAccountAsync(
            identifierOfTheLoggedInUser,
            secretOfThePhoneEncryptedForTheDatabase,
            backupCodesToSaveInTheDatabase,
            ipAddressOfTheLoggedInUser);

        storeOfTheSecretsWaitingForActivation.RemoveSecretWaitingForActivationOfThisAccount(identifierOfTheLoggedInUser);

        if (!activationWasSavedInTheDatabase)
        {
            return new TwoFactorActivationFinishResult { ActivationMustRestartErrorMessage = ErrorMessageForActivationThatMustRestart };
        }

        return new TwoFactorActivationFinishResult { BackupCodesToGiveOnlyOnce = ConvertBackupCodesIntoTheirDisplayedForm(backupCodesInTheirCleanForm) };
    }

    // Enregistre l'activation. Si une autre requête a changé le compte, relit le compte et recommence.
    // Renvoie faux si le compte a disparu, s'il vient d'être activé par une autre requête, ou s'il change encore au bout de 10 passages.
    private async Task<bool> TryToSaveActivationUnlessOtherRequestsKeepChangingTheAccountAsync(
        Guid identifierOfTheLoggedInUser,
        string secretOfThePhoneEncryptedForTheDatabase,
        List<BackupCode> backupCodesToSaveInTheDatabase,
        IPAddress? ipAddressOfTheLoggedInUser)
    {
        for (var numberOfTheTryBeingDone = 1; numberOfTheTryBeingDone <= AccountLockoutRules.MaximumNumberOfTriesWhileOtherRequestsChangeTheAccount; numberOfTheTryBeingDone++)
        {
            var accountReadBeforeSavingTheActivation = await repositoryOfTheUserAccountsInDatabase.FindUserAccountByIdentifierAsync(identifierOfTheLoggedInUser);

            if (accountReadBeforeSavingTheActivation == null || accountReadBeforeSavingTheActivation.IsTwoFactorEnabled)
            {
                return false;
            }

            var activationWasSavedInTheDatabase = await repositoryOfTheTwoFactorDataInDatabase.TryToSaveTwoFactorActivationOfThisAccountAsync(
                accountReadBeforeSavingTheActivation,
                secretOfThePhoneEncryptedForTheDatabase,
                backupCodesToSaveInTheDatabase,
                CreateSecurityEventOfThisActivationAttempt(SecurityEventResult.Success, identifierOfTheLoggedInUser, ipAddressOfTheLoggedInUser));

            if (activationWasSavedInTheDatabase)
            {
                return true;
            }
        }

        return false;
    }

    // Tire au hasard les 10 codes de secours (GR Auth).
    private List<string> GenerateNewBackupCodesInTheirCleanForm()
    {
        var backupCodesInTheirCleanForm = new List<string>();

        for (var numberOfTheBackupCodeBeingDrawn = 1; numberOfTheBackupCodeBeingDrawn <= TwoFactorAuthenticationRules.NumberOfBackupCodesGivenAtEachActivation; numberOfTheBackupCodeBeingDrawn++)
        {
            backupCodesInTheirCleanForm.Add(generatorOfTheSecureRandomTokens.GenerateNewRandomBackupCodeInItsCleanForm());
        }

        return backupCodesInTheirCleanForm;
    }

    // Hache chaque code de secours comme un mot de passe : seule l'empreinte va en base (SS §2).
    private List<BackupCode> HashBackupCodesForThisAccount(List<string> backupCodesInTheirCleanForm, Guid identifierOfTheLoggedInUser)
    {
        var backupCodesToSaveInTheDatabase = new List<BackupCode>();

        foreach (var backupCodeInItsCleanFormBeingHashed in backupCodesInTheirCleanForm)
        {
            backupCodesToSaveInTheDatabase.Add(new BackupCode
            {
                CodeHash = hasherOfThePasswordsAndBackupCodes.ConvertPasswordIntoItsSecureHash(backupCodeInItsCleanFormBeingHashed),
                UserId = identifierOfTheLoggedInUser
            });
        }

        return backupCodesToSaveInTheDatabase;
    }

    // Écrit les codes de secours comme ils sont remis à l'utilisateur.
    private static List<string> ConvertBackupCodesIntoTheirDisplayedForm(List<string> backupCodesInTheirCleanForm)
    {
        var backupCodesInTheirDisplayedForm = new List<string>();

        foreach (var backupCodeInItsCleanFormBeingWritten in backupCodesInTheirCleanForm)
        {
            backupCodesInTheirDisplayedForm.Add(TwoFactorAuthenticationRules.ConvertBackupCodeIntoItsDisplayedForm(backupCodeInItsCleanFormBeingWritten));
        }

        return backupCodesInTheirDisplayedForm;
    }

    // Note un refus dans le journal de sécurité (SS §11).
    private async Task SaveSecurityEventOfThisActivationAttemptAsync(SecurityEventResult resultOfThisAttemptToActivateTheTwoFactor, Guid identifierOfTheLoggedInUser, IPAddress? ipAddressOfTheLoggedInUser)
    {
        await repositoryOfTheSecurityEventsInDatabase.SaveSecurityEventInTheJournalAsync(CreateSecurityEventOfThisActivationAttempt(resultOfThisAttemptToActivateTheTwoFactor, identifierOfTheLoggedInUser, ipAddressOfTheLoggedInUser));
    }

    // Crée l'événement du journal de sécurité (SS §11).
    private SecurityEvent CreateSecurityEventOfThisActivationAttempt(SecurityEventResult resultOfThisAttemptToActivateTheTwoFactor, Guid identifierOfTheLoggedInUser, IPAddress? ipAddressOfTheLoggedInUser)
    {
        return new SecurityEvent
        {
            Type = SecurityEventType.TwoFactorActivation,
            Result = resultOfThisAttemptToActivateTheTwoFactor,
            OccurredAt = clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime,
            IpAddress = ipAddressOfTheLoggedInUser,
            UserId = identifierOfTheLoggedInUser
        };
    }
}
