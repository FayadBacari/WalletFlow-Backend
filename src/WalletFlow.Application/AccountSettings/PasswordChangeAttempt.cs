using System.Net;

namespace WalletFlow.Application.AccountSettings;

// Ce que le changement de mot de passe reçoit de la route.
// Une classe plutôt qu'un record : un record afficherait les mots de passe si on l'écrivait dans un journal.
public class PasswordChangeAttempt
{
    // L'identifiant vient de la session, jamais de la requête (SS §4).
    public required Guid UserId { get; init; }
    public required string CurrentPassword { get; init; }
    public required string NewPassword { get; init; }

    // Le code de l'application ou un code de secours : seulement si la double authentification est activée.
    public string? Code { get; init; }

    public IPAddress? IpAddress { get; init; }
}
