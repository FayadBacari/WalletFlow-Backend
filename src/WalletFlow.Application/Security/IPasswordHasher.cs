namespace WalletFlow.Application.Security;

// Hachage des mots de passe (SS §2). Implémenté dans la couche Infrastructure.
// Chaque calcul est lent et prend beaucoup de mémoire : quand trop de calculs tournent déjà, il attend son tour.
public interface IPasswordHasher
{
    // Transforme un mot de passe en empreinte, seule valeur enregistrée en base.
    Task<string> HashAsync(string password);

    // Renvoie vrai si le mot de passe correspond à l'empreinte enregistrée en base.
    Task<bool> VerifyAsync(string password, string storedHash);
}
