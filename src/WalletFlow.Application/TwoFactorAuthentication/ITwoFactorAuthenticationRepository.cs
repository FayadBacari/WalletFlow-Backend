using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.TwoFactorAuthentication;

// Accès à la base pour la double authentification. Implémenté dans la couche Infrastructure.
public interface ITwoFactorAuthenticationRepository
{
    // Donne les codes de secours de ce compte qui n'ont pas encore servi.
    Task<List<BackupCode>> FindUnusedBackupCodesOfThisAccountAsync(Guid identifierOfTheAccountOwningTheCodes);

    // Cherche un code de secours par son identifiant. Renvoie null s'il n'existe plus.
    Task<BackupCode?> FindBackupCodeByItsIdentifierAsync(Guid identifierOfTheBackupCodeToFind);

    // Enregistre l'activation en une seule transaction SQL : le secret chiffré, les codes de secours et l'événement du journal.
    // Renvoie faux si une autre requête vient de changer le compte : rien n'est alors enregistré.
    Task<bool> TryToSaveTwoFactorActivationOfThisAccountAsync(
        User accountBeingActivatedByTheFirstCode,
        string secretOfThePhoneEncryptedForTheDatabase,
        List<BackupCode> newBackupCodesOfTheAccount,
        SecurityEvent securityEventOfTheTwoFactorActivation);

    // Enregistre la désactivation en une seule transaction SQL : le secret et tous les codes de secours sont effacés,
    // et l'événement est écrit au journal. Renvoie faux si une autre requête vient de changer le compte ou ses codes : rien n'est alors enregistré.
    Task<bool> TryToSaveTwoFactorDeactivationOfThisAccountAsync(User accountBeingDeactivatedByItsOwner, SecurityEvent securityEventOfTheTwoFactorDeactivation);
}
