using System.Net;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.TwoFactorAuthentication;

// Cas d'utilisation : un utilisateur connecté désactive la double authentification avec son mot de passe et un code (GR Auth, SS §2).
// Le code peut être un code de secours : c'est le seul moyen de changer de téléphone après en avoir perdu un.
public class TwoFactorDeactivationService
{
    // Le même message pour un mauvais mot de passe et pour un mauvais code : une session volée ne peut pas servir à tester des mots de passe un par un.
    public const string ErrorMessageForWrongPasswordOrCode = "Le mot de passe ou le code n'est pas bon.";
    public const string ErrorMessageForTwoFactorNotEnabled = "La double authentification n'est pas activée.";

    private readonly IUserAccountRepository repositoryOfTheUserAccountsInDatabase;
    private readonly ITwoFactorAuthenticationRepository repositoryOfTheTwoFactorDataInDatabase;
    private readonly ISecurityEventRepository repositoryOfTheSecurityEventsInDatabase;
    private readonly IPasswordHasher hasherOfThePasswordsTypedByUsers;
    private readonly TwoFactorCodeCheckingService serviceThatChecksTheTwoFactorCodes;
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;

    // Reçoit les outils dont la désactivation a besoin.
    public TwoFactorDeactivationService(
        IUserAccountRepository repositoryOfTheUserAccountsInDatabase,
        ITwoFactorAuthenticationRepository repositoryOfTheTwoFactorDataInDatabase,
        ISecurityEventRepository repositoryOfTheSecurityEventsInDatabase,
        IPasswordHasher hasherOfThePasswordsTypedByUsers,
        TwoFactorCodeCheckingService serviceThatChecksTheTwoFactorCodes,
        TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.repositoryOfTheUserAccountsInDatabase = repositoryOfTheUserAccountsInDatabase;
        this.repositoryOfTheTwoFactorDataInDatabase = repositoryOfTheTwoFactorDataInDatabase;
        this.repositoryOfTheSecurityEventsInDatabase = repositoryOfTheSecurityEventsInDatabase;
        this.hasherOfThePasswordsTypedByUsers = hasherOfThePasswordsTypedByUsers;
        this.serviceThatChecksTheTwoFactorCodes = serviceThatChecksTheTwoFactorCodes;
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Vérifie le mot de passe puis le code : un mauvais mot de passe ne consomme donc pas le code. Si tout est bon, désactive.
    public async Task<TwoFactorDeactivationResult> DeactivateTwoFactorWithPasswordAndCodeOfTheUserAsync(
        Guid identifierOfTheLoggedInUser,
        string passwordTypedByTheLoggedInUser,
        string codeTypedByTheLoggedInUser,
        IPAddress? ipAddressOfTheLoggedInUser)
    {
        var accountOfTheLoggedInUser = await repositoryOfTheUserAccountsInDatabase.FindUserAccountByIdentifierAsync(identifierOfTheLoggedInUser);

        if (accountOfTheLoggedInUser == null || !accountOfTheLoggedInUser.IsTwoFactorEnabled)
        {
            return new TwoFactorDeactivationResult { NotEnabledErrorMessage = ErrorMessageForTwoFactorNotEnabled };
        }

        var secureHashOfThePasswordJustChecked = accountOfTheLoggedInUser.PasswordHash;
        var encryptedTotpSecretOfTheCodeJustChecked = accountOfTheLoggedInUser.EncryptedTotpSecret;
        var passwordWrittenInItsNormalizedForm = PasswordRules.ConvertPasswordIntoItsNormalizedForm(passwordTypedByTheLoggedInUser);

        if (!hasherOfThePasswordsTypedByUsers.CheckThatPasswordMatchesItsSecureHash(passwordWrittenInItsNormalizedForm, secureHashOfThePasswordJustChecked))
        {
            return await RefuseDeactivationWithWrongPasswordOrCodeAsync(identifierOfTheLoggedInUser, ipAddressOfTheLoggedInUser);
        }

        var resultOfTheCheckOfTheCode = await serviceThatChecksTheTwoFactorCodes.CheckThatCodeIsValidForThisActivatedAccountAsync(accountOfTheLoggedInUser, codeTypedByTheLoggedInUser);

        if (!resultOfTheCheckOfTheCode.IsCodeValid)
        {
            return await RefuseDeactivationWithWrongPasswordOrCodeAsync(identifierOfTheLoggedInUser, ipAddressOfTheLoggedInUser);
        }

        return await SaveDeactivationUnlessOtherRequestsKeepChangingTheAccountAsync(identifierOfTheLoggedInUser, secureHashOfThePasswordJustChecked, encryptedTotpSecretOfTheCodeJustChecked, ipAddressOfTheLoggedInUser);
    }

    // Enregistre la désactivation. Si une autre requête a changé le compte, relit le compte et recommence.
    // Le mot de passe et le secret qui viennent d'être vérifiés doivent être encore les bons : une récupération a pu passer entre-temps.
    private async Task<TwoFactorDeactivationResult> SaveDeactivationUnlessOtherRequestsKeepChangingTheAccountAsync(
        Guid identifierOfTheLoggedInUser,
        string secureHashOfThePasswordJustChecked,
        string? encryptedTotpSecretOfTheCodeJustChecked,
        IPAddress? ipAddressOfTheLoggedInUser)
    {
        for (var numberOfTheTryBeingDone = 1; numberOfTheTryBeingDone <= AccountLockoutRules.MaximumNumberOfTriesWhileOtherRequestsChangeTheAccount; numberOfTheTryBeingDone++)
        {
            var accountReadBeforeSavingTheDeactivation = await repositoryOfTheUserAccountsInDatabase.FindUserAccountByIdentifierAsync(identifierOfTheLoggedInUser);

            if (accountReadBeforeSavingTheDeactivation == null || !accountReadBeforeSavingTheDeactivation.IsTwoFactorEnabled)
            {
                return new TwoFactorDeactivationResult { NotEnabledErrorMessage = ErrorMessageForTwoFactorNotEnabled };
            }

            if (accountReadBeforeSavingTheDeactivation.PasswordHash != secureHashOfThePasswordJustChecked
                || accountReadBeforeSavingTheDeactivation.EncryptedTotpSecret != encryptedTotpSecretOfTheCodeJustChecked)
            {
                return await RefuseDeactivationWithWrongPasswordOrCodeAsync(identifierOfTheLoggedInUser, ipAddressOfTheLoggedInUser);
            }

            var deactivationWasSavedInTheDatabase = await repositoryOfTheTwoFactorDataInDatabase.TryToSaveTwoFactorDeactivationOfThisAccountAsync(
                accountReadBeforeSavingTheDeactivation,
                CreateSecurityEventOfThisDeactivationAttempt(SecurityEventResult.Success, identifierOfTheLoggedInUser, ipAddressOfTheLoggedInUser));

            if (deactivationWasSavedInTheDatabase)
            {
                return new TwoFactorDeactivationResult();
            }
        }

        return await RefuseDeactivationWithWrongPasswordOrCodeAsync(identifierOfTheLoggedInUser, ipAddressOfTheLoggedInUser);
    }

    // Note le refus dans le journal, et donne le même message dans tous les cas.
    private async Task<TwoFactorDeactivationResult> RefuseDeactivationWithWrongPasswordOrCodeAsync(Guid identifierOfTheLoggedInUser, IPAddress? ipAddressOfTheLoggedInUser)
    {
        await repositoryOfTheSecurityEventsInDatabase.SaveSecurityEventInTheJournalAsync(CreateSecurityEventOfThisDeactivationAttempt(SecurityEventResult.Failure, identifierOfTheLoggedInUser, ipAddressOfTheLoggedInUser));

        return new TwoFactorDeactivationResult { WrongPasswordOrCodeErrorMessage = ErrorMessageForWrongPasswordOrCode };
    }

    // Crée l'événement du journal de sécurité (SS §11).
    private SecurityEvent CreateSecurityEventOfThisDeactivationAttempt(SecurityEventResult resultOfThisAttemptToDeactivateTheTwoFactor, Guid identifierOfTheLoggedInUser, IPAddress? ipAddressOfTheLoggedInUser)
    {
        return new SecurityEvent
        {
            Type = SecurityEventType.TwoFactorDeactivation,
            Result = resultOfThisAttemptToDeactivateTheTwoFactor,
            OccurredAt = clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime,
            IpAddress = ipAddressOfTheLoggedInUser,
            UserId = identifierOfTheLoggedInUser
        };
    }
}
