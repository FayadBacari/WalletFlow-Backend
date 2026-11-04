using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserAccounts;

// Accès à la base pour les comptes des utilisateurs. Implémenté dans la couche Infrastructure.
public interface IUserAccountRepository
{
    // Renvoie vrai si un compte utilise déjà cette adresse e-mail.
    Task<bool> CheckThatEmailAddressIsAlreadyUsedAsync(string emailAddressInItsStoredForm);

    // Enregistre le nouvel utilisateur, ses catégories et son jeton en une seule transaction SQL (MPD §3).
    // Renvoie faux si une autre inscription vient de prendre la même adresse.
    Task<bool> TryToSaveNewAccountWithCategoriesAndTokenAsync(
        User newUserToRegisterInTheDatabase,
        List<Category> defaultCategoriesOfTheNewUser,
        EmailToken signupConfirmationTokenOfTheNewUser);
}
