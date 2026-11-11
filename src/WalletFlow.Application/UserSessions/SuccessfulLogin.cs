using System.Net;

namespace WalletFlow.Application.UserSessions;

// Ce qu'il faut pour ouvrir la session d'une connexion réussie : ce qui vient d'être vérifié, l'adresse IP et les cookies déjà présents.
// Une classe plutôt qu'un record : un record afficherait les jetons si on l'écrivait dans un journal.
public class SuccessfulLogin
{
    public required Guid AccountId { get; init; }
    public required string PasswordHash { get; init; }
    public string? EncryptedTotpSecret { get; init; }
    public Guid? BackupCodeId { get; init; }
    public IPAddress? IpAddress { get; init; }
    public string? DeviceToken { get; init; }
    public string? SessionToken { get; init; }
}
