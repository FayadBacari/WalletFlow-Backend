namespace WalletFlow.Application.BankConnections;

// Les raisons d'un échec du prestataire bancaire.
public enum BankProviderFailure
{
    Unavailable,          // le prestataire ou la banque ne répond pas, ou répond une erreur : on réessaiera plus tard
    SessionEnded,         // la session a expiré, ou la banque l'a retirée : il faut une nouvelle autorisation
    RateLimited,          // la banque refuse pour l'instant : trop de lectures faites sans l'utilisateur
    AuthorizationRefused  // le code renvoyé par la banque est faux, expiré ou déjà utilisé
}
