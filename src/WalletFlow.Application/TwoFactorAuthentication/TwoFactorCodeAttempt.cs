using System.Net;

namespace WalletFlow.Application.TwoFactorAuthentication;

// Ce que la fin de la connexion reçoit de la route : le code tapé, l'adresse IP et les cookies déjà présents.
// Une classe plutôt qu'un record : un record afficherait le code si on l'écrivait dans un journal.
public class TwoFactorCodeAttempt
{
    public required string Code { get; init; }
    public IPAddress? IpAddress { get; init; }
    public string? WaitingLoginToken { get; init; }
    public string? DeviceToken { get; init; }
    public string? SessionToken { get; init; }
}
