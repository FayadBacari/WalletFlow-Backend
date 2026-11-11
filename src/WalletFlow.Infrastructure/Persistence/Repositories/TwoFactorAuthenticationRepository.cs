using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Lit et écrit dans la base PostgreSQL les codes de secours de la double authentification.
// Les méthodes « Prepare… » n'écrivent rien : l'unité de travail enregistre tout ensemble (voir IDatabaseUnitOfWork).
public class TwoFactorAuthenticationRepository : ITwoFactorAuthenticationRepository
{
    private readonly WalletFlowDatabaseContext databaseContext;

    // Reçoit l'accès à la base de la requête en cours.
    public TwoFactorAuthenticationRepository(WalletFlowDatabaseContext databaseContext)
    {
        this.databaseContext = databaseContext;
    }

    // Donne les codes de secours de ce compte qui n'ont pas encore servi. Ils ne servent qu'à être lus : EF Core ne les suit pas.
    public async Task<List<BackupCode>> FindUnusedBackupCodesAsync(Guid accountId)
    {
        return await databaseContext.BackupCodes.AsNoTracking()
            .Where(backupCode =>
                backupCode.UserId == accountId
                && backupCode.UsedAt == null)
            .ToListAsync();
    }

    // Cherche un code de secours par son identifiant. EF Core le suit : la connexion peut ainsi le marquer comme utilisé.
    public async Task<BackupCode?> FindBackupCodeAsync(Guid backupCodeId)
    {
        return await databaseContext.BackupCodes.SingleOrDefaultAsync(backupCode =>
            backupCode.Id == backupCodeId);
    }

    // Prépare l'enregistrement des codes de secours d'une activation.
    public void PrepareBackupCodes(List<BackupCode> backupCodes)
    {
        databaseContext.BackupCodes.AddRange(backupCodes);
    }

    // Prépare l'effacement de tous les codes de secours du compte, utilisés ou non. EF Core suit ces codes :
    // si une autre requête vient de se servir de l'un d'eux, l'enregistrement échoue.
    public async Task PrepareBackupCodesRemovalAsync(Guid accountId)
    {
        var backupCodes = await databaseContext.BackupCodes
            .Where(backupCode => backupCode.UserId == accountId)
            .ToListAsync();

        databaseContext.BackupCodes.RemoveRange(backupCodes);
    }
}
