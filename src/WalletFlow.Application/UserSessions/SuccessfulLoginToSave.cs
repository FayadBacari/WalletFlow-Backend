using System.Net;

namespace WalletFlow.Application.UserSessions;

// Ce qu'il faut pour ouvrir la session d'une connexion réussie : ce qui vient d'être vérifié, l'adresse IP et les cookies déjà présents.
// Une classe plutôt qu'un record : un record afficherait les jetons si on l'écrivait dans un journal.
public class SuccessfulLoginToSave
{
    public required Guid IdentifierOfTheAccountLoggingIn { get; init; }
    public required string SecureHashOfThePasswordJustChecked { get; init; }
    public string? EncryptedTotpSecretJustChecked { get; init; }
    public Guid? IdentifierOfTheBackupCodeJustUsed { get; init; }
    public IPAddress? IpAddressOfTheVisitor { get; init; }
    public string? DeviceTokenReadInTheCookie { get; init; }
    public string? SessionTokenReadInTheCookie { get; init; }
}
