using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserAccounts;

// Accès à la base pour les comptes des utilisateurs. Implémenté dans la couche Infrastructure.
public interface IUserAccountRepository
{
    // Cherche le compte qui utilise cette adresse e-mail. Renvoie null s'il n'y en a pas.
    Task<User?> FindUserAccountByEmailAddressAsync(string emailAddressInItsStoredForm);

    // Cherche un compte par son identifiant. Renvoie null s'il n'existe plus.
    Task<User?> FindUserAccountByIdentifierAsync(Guid identifierOfTheUserAccountToFind);

    // Cherche un jeton de confirmation d'inscription par son empreinte. Renvoie null s'il n'existe pas.
    Task<EmailToken?> FindSignupConfirmationTokenByItsHashAsync(string hashOfTheTokenFromTheLink);

    // Enregistre le nouvel utilisateur, ses catégories et son jeton en une seule transaction SQL (MPD §3).
    // Renvoie faux si une autre inscription vient de prendre la même adresse.
    Task<bool> TryToSaveNewAccountWithCategoriesAndTokenAsync(
        User newUserToRegisterInTheDatabase,
        List<Category> defaultCategoriesOfTheNewUser,
        EmailToken signupConfirmationTokenOfTheNewUser);

    // Remplace le mot de passe et le lien d'un compte non confirmé, en une seule transaction SQL.
    // Renvoie faux si le compte vient d'être confirmé ou effacé : rien n'est alors modifié.
    Task<bool> TryToReplacePasswordAndTokenOfUnconfirmedAccountAsync(
        User unconfirmedAccountToUpdateInTheDatabase,
        string newSecureHashOfThePassword,
        EmailToken newSignupConfirmationTokenOfTheAccount);

    // Confirme le compte et marque son jeton comme utilisé, en une seule transaction SQL.
    // Renvoie faux si le compte ou le jeton vient de changer : rien n'est alors modifié.
    Task<bool> TryToConfirmAccountAndUseItsTokenAsync(
        User accountToConfirmInTheDatabase,
        EmailToken tokenUsedToConfirmTheAccount,
        DateTime dateOfTheConfirmationOfTheAccount);

    // Enregistre les compteurs de connexion du compte.
    // Renvoie faux si une autre requête vient de changer ce compte : rien n'est alors modifié.
    Task<bool> TryToSaveLoginCountersOfThisAccountAsync(User accountWithItsNewLoginCounters);

    // Efface les comptes non confirmés qui n'ont plus aucun lien valable, et renvoie leur nombre.
    Task<int> DeleteUnconfirmedAccountsWithExpiredLinkAsync(DateTime currentDateUsedForTheCleanup);
}
