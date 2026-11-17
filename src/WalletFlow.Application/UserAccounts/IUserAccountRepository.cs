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

    // Prépare l'effacement des liens de récupération, de déblocage et de désactivation de la double authentification du compte,
    // à la confirmation d'une nouvelle adresse : ils sont partis vers l'ancienne (SS §2).
    Task PrepareOldAddressLinksRemovalAsync(Guid accountId);

    // Demande que le prochain enregistrement contrôle ce compte même si rien n'y a changé :
    // il échouera si une autre requête a modifié le compte depuis sa lecture.
    void PrepareUnchangedCheck(User account);

    // Efface tout de suite les comptes non confirmés qui n'ont plus aucun lien valable, et renvoie leur nombre.
    Task<int> DeleteExpiredUnconfirmedAccountsAsync(DateTime now);

    // Prépare l'effacement de tous les liens du compte, quel que soit leur type : la demande de suppression les annule (SS §7).
    Task PrepareAllLinksRemovalAsync(Guid accountId);

    // Donne les comptes dont la suppression est demandée à cette date ou avant, du plus ancien au plus récent : la tâche de
    // fond les efface. Rien n'est suivi.
    Task<List<Guid>> FindAccountsToEraseAsync(DateTime requestedAtOrBefore);

    // Prépare l'effacement définitif de ce compte, lu par cet accès à la base : tout ce qui lui appartient part avec lui, et le
    // journal garde ses événements sans lui (MPD). L'enregistrement échoue si une autre requête l'a changé depuis sa lecture.
    void PrepareErasure(User account);
}
