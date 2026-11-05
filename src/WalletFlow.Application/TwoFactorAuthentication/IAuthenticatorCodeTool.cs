namespace WalletFlow.Application.TwoFactorAuthentication;

// Calcul des codes de l'application d'authentification (TOTP, SS §2). Implémenté dans la couche Infrastructure.
public interface IAuthenticatorCodeTool
{
    // Tire au hasard un nouveau secret, écrit comme l'attendent les applications d'authentification.
    string GenerateNewRandomSecretForTheAuthenticatorApplication();

    // Donne le lien otpauth:// que le site transforme lui-même en code QR : le secret ne part vers aucun service extérieur.
    string BuildLinkThatAddsThisSecretToTheApplication(string secretSharedWithTheAuthenticatorApplication, string emailAddressOfTheUserAccount);

    // Donne le numéro de la période de 30 secondes où ce code est valable : celle en cours, la précédente ou la suivante (SS §2).
    // Renvoie null si le code ne vaut pour aucune des trois. La date est en UTC.
    long? FindPeriodWhereThisCodeIsValidAtThisDate(string secretSharedWithTheAuthenticatorApplication, string authenticatorCodeTypedByTheUserToCheckNow, DateTime dateOfTheCodeCheckInUtc);
}
