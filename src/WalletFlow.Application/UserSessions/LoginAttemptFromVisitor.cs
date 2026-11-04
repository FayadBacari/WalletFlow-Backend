using System.Net;

namespace WalletFlow.Application.UserSessions;

// Ce que la connexion reçoit de la route : les deux champs tapés, l'adresse IP et les cookies déjà présents.
// Une classe plutôt qu'un record : un record afficherait le mot de passe si on l'écrivait dans un journal.
public class LoginAttemptFromVisitor
{
    public required string EmailAddressTypedByTheVisitor { get; init; }
    public required string PasswordTextTypedByTheVisitor { get; init; }
    public IPAddress? IpAddressOfTheVisitor { get; init; }
    public string? DeviceTokenReadInTheCookie { get; init; }
    public string? SessionTokenReadInTheCookie { get; init; }
}
