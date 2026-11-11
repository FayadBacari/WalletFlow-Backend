using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserAccounts;

// Accès à la base pour les comptes des utilisateurs et leurs liens. Implémenté dans la couche Infrastructure.
// Les objets lus ici sont suivis, et les méthodes « Prepare… » n'écrivent rien : voir IDatabaseUnitOfWork.
public interface IUserAccountRepository
{
    // Cherche le compte qui utilise cette adresse e-mail. Renvoie null s'il n'y en a pas.
    Task<User?> FindByEmailAsync(string email);

    // Cherche un compte par son identifiant. Renvoie null s'il n'existe plus.
    Task<User?> FindByIdAsync(Guid accountId);

    // Cherche un jeton de ce type par son empreinte. Renvoie null s'il n'existe pas : un lien ne sert jamais à un autre usage.
    Task<EmailToken?> FindTokenAsync(string tokenHash, EmailTokenType tokenType);

    // Enregistre tout de suite le nouvel utilisateur, ses catégories et son jeton, en une seule transaction SQL (MPD §3).
    // Renvoie faux si une autre inscription vient de prendre la même adresse : rien n'est alors enregistré.
    Task<bool> TryAddAccountAsync(
        User newAccount,
        List<Category> categories,
        EmailToken confirmationToken);

    // Efface tout de suite l'ancien jeton du même type et du même compte, puis enregistre le nouveau : l'ancien lien ne marche plus (SS §2).
    Task ReplaceTokenAsync(EmailToken newToken);

    // Prépare le remplacement du lien de confirmation d'un compte : ses anciens liens seront effacés, et le nouveau enregistré.
    Task PrepareConfirmationTokenReplacementAsync(EmailToken newToken);

    // Demande que le prochain enregistrement contrôle ce compte même si rien n'y a changé :
    // il échouera si une autre requête a modifié le compte depuis sa lecture.
    void PrepareUnchangedCheck(User account);

    // Efface tout de suite les comptes non confirmés qui n'ont plus aucun lien valable, et renvoie leur nombre.
    Task<int> DeleteExpiredUnconfirmedAccountsAsync(DateTime now);
}
