using System.Net;

namespace WalletFlow.Application.UserSessions;

// Ce que la connexion reçoit de la route : les deux champs tapés, l'adresse IP et les cookies déjà présents.
// Une classe plutôt qu'un record : un record afficherait le mot de passe si on l'écrivait dans un journal.
public class LoginAttempt
{
    public required string EmailAddress { get; init; }
    public required string Password { get; init; }
    public IPAddress? IpAddress { get; init; }
    public string? DeviceToken { get; init; }
    public string? SessionToken { get; init; }
}
