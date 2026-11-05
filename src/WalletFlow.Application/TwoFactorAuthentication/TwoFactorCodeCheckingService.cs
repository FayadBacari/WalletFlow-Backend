using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.TwoFactorAuthentication;

// Vérifie un code de double authentification : un code de l'application, ou un code de secours (GR Auth, SS §2).
public class TwoFactorCodeCheckingService
{
    public const string ErrorMessageForWrongTwoFactorCode = "Ce code n'est pas bon. Vérifie-le, ou attends le code suivant.";

    private readonly ITwoFactorAuthenticationRepository repositoryOfTheBackupCodesInDatabase;
    private readonly IPasswordHasher hasherOfTheBackupCodesTypedByUsers;
    private readonly IAuthenticatorCodeTool toolThatCalculatesTheAuthenticatorCodes;
    private readonly ITotpSecretProtector protectorOfTheSecretsInTheDatabase;
    private readonly IUsedAuthenticatorCodesRegister registerOfTheAuthenticatorCodesAlreadyUsed;
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;

    // Reçoit les outils dont la vérification a besoin.
    public TwoFactorCodeCheckingService(
        ITwoFactorAuthenticationRepository repositoryOfTheBackupCodesInDatabase,
        IPasswordHasher hasherOfTheBackupCodesTypedByUsers,
        IAuthenticatorCodeTool toolThatCalculatesTheAuthenticatorCodes,
        ITotpSecretProtector protectorOfTheSecretsInTheDatabase,
        IUsedAuthenticatorCodesRegister registerOfTheAuthenticatorCodesAlreadyUsed,
        TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.repositoryOfTheBackupCodesInDatabase = repositoryOfTheBackupCodesInDatabase;
        this.hasherOfTheBackupCodesTypedByUsers = hasherOfTheBackupCodesTypedByUsers;
        this.toolThatCalculatesTheAuthenticatorCodes = toolThatCalculatesTheAuthenticatorCodes;
        this.protectorOfTheSecretsInTheDatabase = protectorOfTheSecretsInTheDatabase;
        this.registerOfTheAuthenticatorCodesAlreadyUsed = registerOfTheAuthenticatorCodesAlreadyUsed;
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Vérifie un code pour un compte déjà activé. Un texte qui n'a la forme d'aucun code est refusé sans rien calculer.
    // Le secret n'est déchiffré que pour un code de l'application : si la clé est perdue, les codes de secours marchent encore.
    public async Task<TwoFactorCodeCheckResult> CheckThatCodeIsValidForThisActivatedAccountAsync(User accountWithTheTwoFactorAlreadyActivated, string codeTypedByTheUserInTheForm)
    {
        var codeWrittenInItsCleanForm = TwoFactorAuthenticationRules.ConvertCodeTypedByTheUserIntoItsCleanForm(codeTypedByTheUserInTheForm);

        if (TwoFactorAuthenticationRules.CheckThatTextHasTheFormatOfAuthenticatorCode(codeWrittenInItsCleanForm))
        {
            var secretSharedWithTheAuthenticatorApplication = protectorOfTheSecretsInTheDatabase.DecryptSecretOfThisUserAccount(accountWithTheTwoFactorAlreadyActivated.EncryptedTotpSecret!, accountWithTheTwoFactorAlreadyActivated.Id);

            return new TwoFactorCodeCheckResult
            {
                IsCodeValid = CheckThatCodeMatchesThisSecretAndWasNeverUsed(accountWithTheTwoFactorAlreadyActivated.Id, secretSharedWithTheAuthenticatorApplication, codeWrittenInItsCleanForm)
            };
        }

        if (TwoFactorAuthenticationRules.CheckThatTextHasTheFormatOfOneBackupCode(codeWrittenInItsCleanForm))
        {
            return await CheckThatCodeIsOneUnusedBackupCodeOfTheAccountAsync(accountWithTheTwoFactorAlreadyActivated.Id, codeWrittenInItsCleanForm);
        }

        return new TwoFactorCodeCheckResult { IsCodeValid = false };
    }

    // Vérifie le premier code, à l'activation : seul un code de l'application peut convenir, le compte n'a pas encore de code de secours.
    public bool CheckThatFirstCodeMatchesTheSecretWaitingForActivation(Guid identifierOfTheAccountBeingActivated, string secretSharedWithTheAuthenticatorApplication, string codeTypedByTheUserInTheForm)
    {
        var codeWrittenInItsCleanForm = TwoFactorAuthenticationRules.ConvertCodeTypedByTheUserIntoItsCleanForm(codeTypedByTheUserInTheForm);

        return CheckThatCodeMatchesThisSecretAndWasNeverUsed(identifierOfTheAccountBeingActivated, secretSharedWithTheAuthenticatorApplication, codeWrittenInItsCleanForm);
    }

    // Compare le code au secret, puis le note comme utilisé : un code déjà utilisé reçoit la même réponse qu'un mauvais code (SS §2).
    private bool CheckThatCodeMatchesThisSecretAndWasNeverUsed(Guid identifierOfTheAccountUsingTheCode, string secretSharedWithTheAuthenticatorApplication, string codeWrittenInItsCleanForm)
    {
        var numberOfThePeriodWhereTheCodeIsValid = toolThatCalculatesTheAuthenticatorCodes.FindPeriodWhereThisCodeIsValidAtThisDate(
            secretSharedWithTheAuthenticatorApplication,
            codeWrittenInItsCleanForm,
            clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime);

        if (numberOfThePeriodWhereTheCodeIsValid == null)
        {
            return false;
        }

        return registerOfTheAuthenticatorCodesAlreadyUsed.TryToRecordThisPeriodAsUsedByThisAccount(identifierOfTheAccountUsingTheCode, numberOfThePeriodWhereTheCodeIsValid.Value);
    }

    // Compare le code à chaque code de secours encore inutilisé : hachés comme des mots de passe, ils ne se cherchent pas autrement (SS §2).
    private async Task<TwoFactorCodeCheckResult> CheckThatCodeIsOneUnusedBackupCodeOfTheAccountAsync(Guid identifierOfTheAccountUsingTheCode, string codeWrittenInItsCleanForm)
    {
        var unusedBackupCodesOfTheAccount = await repositoryOfTheBackupCodesInDatabase.FindUnusedBackupCodesOfThisAccountAsync(identifierOfTheAccountUsingTheCode);

        foreach (var unusedBackupCodeOfTheAccountBeingCompared in unusedBackupCodesOfTheAccount)
        {
            if (hasherOfTheBackupCodesTypedByUsers.CheckThatPasswordMatchesItsSecureHash(codeWrittenInItsCleanForm, unusedBackupCodeOfTheAccountBeingCompared.CodeHash))
            {
                return new TwoFactorCodeCheckResult { IsCodeValid = true, IdentifierOfTheBackupCodeUsed = unusedBackupCodeOfTheAccountBeingCompared.Id };
            }
        }

        return new TwoFactorCodeCheckResult { IsCodeValid = false };
    }
}
