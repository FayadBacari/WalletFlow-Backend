namespace WalletFlow.Application.Security;

// Cherche un mot de passe parmi ceux des fuites de données connues (SS §2) : un attaquant les essaie en premier, sur tous
// les sites. Implémenté dans la couche Infrastructure.
public interface IBreachedPasswordChecker
{
    // Renvoie vrai si ce mot de passe, déjà normalisé, figure dans une fuite connue. Si le service de recherche ne répond pas,
    // renvoie faux : le mot de passe passe, et la panne va au journal technique.
    Task<bool> IsBreachedAsync(string normalizedPassword, CancellationToken cancellationToken = default);
}
