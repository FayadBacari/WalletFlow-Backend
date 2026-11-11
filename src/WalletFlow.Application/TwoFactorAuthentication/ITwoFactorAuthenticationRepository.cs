using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.TwoFactorAuthentication;

// Accès à la base pour les codes de secours de la double authentification. Implémenté dans la couche Infrastructure.
// Les méthodes « Prepare… » n'écrivent rien : voir IDatabaseUnitOfWork.
public interface ITwoFactorAuthenticationRepository
{
    // Donne les codes de secours de ce compte qui n'ont pas encore servi. Ils ne servent qu'à être comparés : ils ne sont pas suivis.
    Task<List<BackupCode>> FindUnusedBackupCodesAsync(Guid accountId);

    // Cherche un code de secours par son identifiant. Renvoie null s'il n'existe plus.
    // Ce code est suivi : la connexion peut ainsi le marquer comme utilisé.
    Task<BackupCode?> FindBackupCodeAsync(Guid backupCodeId);

    // Prépare l'enregistrement des codes de secours d'une activation.
    void PrepareBackupCodes(List<BackupCode> backupCodes);

    // Prépare l'effacement de tous les codes de secours du compte, utilisés ou non :
    // une nouvelle activation en donnera 10 autres (GR Auth).
    Task PrepareBackupCodesRemovalAsync(Guid accountId);
}
