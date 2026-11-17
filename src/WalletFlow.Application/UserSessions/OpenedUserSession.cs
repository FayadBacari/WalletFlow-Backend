namespace WalletFlow.Application.UserSessions;

// La session qui vient de s'ouvrir : les jetons à écrire dans les cookies.
// Une classe plutôt qu'un record : un record afficherait les jetons si on l'écrivait dans un journal.
public class OpenedUserSession
{
    public required string SessionToken { get; init; }
    public required string DeviceToken { get; init; }

    // Vrai si cette connexion a annulé la suppression du compte (GR Suppression).
    public bool HasCancelledDeletion { get; init; } = false;
}
