using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Lit et écrit dans la base PostgreSQL ce qui concerne la double authentification.
public class TwoFactorAuthenticationRepository : ITwoFactorAuthenticationRepository
{
    private readonly WalletFlowDatabaseContext databaseContextOfTheCurrentHttpRequest;

    // Reçoit l'accès à la base de la requête en cours.
    public TwoFactorAuthenticationRepository(WalletFlowDatabaseContext databaseContextOfTheCurrentHttpRequest)
    {
        this.databaseContextOfTheCurrentHttpRequest = databaseContextOfTheCurrentHttpRequest;
    }

    // Donne les codes de secours de ce compte qui n'ont pas encore servi. Ils ne servent qu'à être lus : EF Core ne les suit pas.
    public async Task<List<BackupCode>> FindUnusedBackupCodesOfThisAccountAsync(Guid identifierOfTheAccountOwningTheCodes)
    {
        return await databaseContextOfTheCurrentHttpRequest.BackupCodes.AsNoTracking()
            .Where(backupCodeAlreadySavedInTheDatabase =>
                backupCodeAlreadySavedInTheDatabase.UserId == identifierOfTheAccountOwningTheCodes
                && backupCodeAlreadySavedInTheDatabase.UsedAt == null)
            .ToListAsync();
    }

    // Cherche un code de secours par son identifiant. EF Core le suit : la connexion peut ainsi le marquer comme utilisé.
    public async Task<BackupCode?> FindBackupCodeByItsIdentifierAsync(Guid identifierOfTheBackupCodeToFind)
    {
        return await databaseContextOfTheCurrentHttpRequest.BackupCodes.SingleOrDefaultAsync(backupCodeAlreadySavedInTheDatabase =>
            backupCodeAlreadySavedInTheDatabase.Id == identifierOfTheBackupCodeToFind);
    }

    // Enregistre tout en une seule transaction SQL : SaveChangesAsync enregistre tout, ou rien.
    // Le contrôle sur secret_totp_chiffre renvoie faux si une autre activation vient de réussir : un compte ne reçoit jamais 20 codes.
    public async Task<bool> TryToSaveTwoFactorActivationOfThisAccountAsync(
        User accountBeingActivatedByTheFirstCode,
        string secretOfThePhoneEncryptedForTheDatabase,
        List<BackupCode> newBackupCodesOfTheAccount,
        SecurityEvent securityEventOfTheTwoFactorActivation)
    {
        accountBeingActivatedByTheFirstCode.IsTwoFactorEnabled = true;
        accountBeingActivatedByTheFirstCode.EncryptedTotpSecret = secretOfThePhoneEncryptedForTheDatabase;
        databaseContextOfTheCurrentHttpRequest.BackupCodes.AddRange(newBackupCodesOfTheAccount);
        databaseContextOfTheCurrentHttpRequest.SecurityEvents.Add(securityEventOfTheTwoFactorActivation);

        return await databaseContextOfTheCurrentHttpRequest.TryToSaveChangesUnlessAnotherRequestChangedTheSameRowsAsync();
    }

    // Enregistre tout en une seule transaction SQL : SaveChangesAsync enregistre tout, ou rien.
    // Tous les codes de secours partent, utilisés ou non : une nouvelle activation en donnera 10 autres (GR Auth).
    public async Task<bool> TryToSaveTwoFactorDeactivationOfThisAccountAsync(User accountBeingDeactivatedByItsOwner, SecurityEvent securityEventOfTheTwoFactorDeactivation)
    {
        var allBackupCodesOfTheAccount = await databaseContextOfTheCurrentHttpRequest.BackupCodes
            .Where(backupCodeAlreadySavedInTheDatabase => backupCodeAlreadySavedInTheDatabase.UserId == accountBeingDeactivatedByItsOwner.Id)
            .ToListAsync();

        accountBeingDeactivatedByItsOwner.IsTwoFactorEnabled = false;
        accountBeingDeactivatedByItsOwner.EncryptedTotpSecret = null;
        databaseContextOfTheCurrentHttpRequest.BackupCodes.RemoveRange(allBackupCodesOfTheAccount);
        databaseContextOfTheCurrentHttpRequest.SecurityEvents.Add(securityEventOfTheTwoFactorDeactivation);

        return await databaseContextOfTheCurrentHttpRequest.TryToSaveChangesUnlessAnotherRequestChangedTheSameRowsAsync();
    }
}
