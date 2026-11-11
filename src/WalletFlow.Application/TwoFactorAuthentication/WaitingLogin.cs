namespace WalletFlow.Application.TwoFactorAuthentication;

// Une connexion dont le mot de passe est bon, et qui attend son code. Aucune session n'est encore ouverte (SS §2).
// Une classe plutôt qu'un record : un record afficherait l'empreinte du mot de passe si on l'écrivait dans un journal.
public class WaitingLogin
{
    public required Guid AccountId { get; init; }
    public required string PasswordHash { get; init; }
    public required int TriedCodeCount { get; init; }
}
