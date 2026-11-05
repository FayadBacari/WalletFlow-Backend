using System.Net;

namespace WalletFlow.Application.TwoFactorAuthentication;

// Ce que la fin de la connexion reçoit de la route : le code tapé, l'adresse IP et les cookies déjà présents.
// Une classe plutôt qu'un record : un record afficherait le code si on l'écrivait dans un journal.
public class TwoFactorCodeAttemptFromVisitor
{
    public required string CodeTypedByTheVisitor { get; init; }
    public IPAddress? IpAddressOfTheVisitor { get; init; }
    public string? WaitingLoginTokenReadInTheCookie { get; init; }
    public string? DeviceTokenReadInTheCookie { get; init; }
    public string? SessionTokenReadInTheCookie { get; init; }
}
