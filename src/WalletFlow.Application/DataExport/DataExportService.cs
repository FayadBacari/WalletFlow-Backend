using System.Net;
using WalletFlow.Application.AccountSettings;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.DataExport;

// Cas d'utilisation : l'utilisateur connecté télécharge toutes ses données dans un fichier (GR Compte, droit à la portabilité
// du RGPD ; SS §2 et §6). C'est une action sensible : elle redemande le mot de passe, et un code si la double authentification
// est activée, sinon un voleur de session repartirait avec tout en un clic. Chaque essai compte dans la limite de 3 par heure,
// bon ou mauvais : une session volée ne peut donc pas essayer plus de 3 mots de passe par heure. Chaque téléchargement est noté
// au journal, et chaque refus aussi (SS §11).
public class DataExportService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly IDataExportRepository exportRepository;
    private readonly IDataExportLimiter exportLimiter;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly AccountIdentityCheckingService identityChecking;
    private readonly TimeProvider clock;

    // Reçoit les outils dont le téléchargement a besoin.
    public DataExportService(
        IUserAccountRepository accountRepository,
        IDataExportRepository exportRepository,
        IDataExportLimiter exportLimiter,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        AccountIdentityCheckingService identityChecking,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.exportRepository = exportRepository;
        this.exportLimiter = exportLimiter;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.identityChecking = identityChecking;
        this.clock = clock;
    }

    // Compte l'essai, vérifie l'identité, note le téléchargement au journal, puis lit les données. Elles ne sont lues qu'une fois
    // le téléchargement noté, et le code de secours dépensé s'il en a servi un.
    public async Task<DataExportResult> ExportAsync(DataExportAttempt attempt)
    {
        if (!exportLimiter.TryCountExport(attempt.UserId))
        {
            return new DataExportResult.TooManyExports();
        }

        var account = await accountRepository.FindByIdAsync(attempt.UserId);
        if (account == null)
        {
            return new DataExportResult.AccountGone();
        }

        // Lus avant la vérification : l'usage d'un code de secours contrôlera qu'ils sont toujours les bons.
        var checkedPasswordHash = account.PasswordHash;
        var checkedTotpSecret = account.EncryptedTotpSecret;

        var identityCheck = await identityChecking.CheckAsync(account, attempt.Password, attempt.Code);
        if (!identityCheck.IsConfirmed)
        {
            return await RefuseAsync(account, attempt.IpAddress);
        }

        var export = new ExportToRecord
        {
            UserId = account.Id,
            CheckedPasswordHash = checkedPasswordHash,
            CheckedTotpSecret = checkedTotpSecret,
            BackupCodeId = identityCheck.BackupCodeId,
            IpAddress = attempt.IpAddress
        };

        if (!await savingService.SaveWithRetriesAsync(() => PrepareRecordAsync(export), false))
        {
            return await RefuseAsync(account, attempt.IpAddress);
        }

        var data = await exportRepository.ReadAsync(account.Id);
        if (data == null)
        {
            return new DataExportResult.AccountGone();
        }

        return new DataExportResult.Exported { Data = data, ExportedAt = clock.GetUtcNow().UtcDateTime };
    }

    // Un passage : prépare le téléchargement au journal. Si un code de secours a servi, relit d'abord le compte, vérifie que le
    // mot de passe et le secret contrôlés sont toujours les bons, et prépare l'usage du code avec son alerte : le code n'est dépensé
    // que si le téléchargement a lieu. Le compte est alors contrôlé à l'enregistrement, même s'il n'a pas changé.
    private async Task<PreparedChanges<bool>> PrepareRecordAsync(ExportToRecord export)
    {
        if (export.BackupCodeId != null)
        {
            var account = await accountRepository.FindByIdAsync(export.UserId);
            if (account == null
                || account.PasswordHash != export.CheckedPasswordHash
                || account.EncryptedTotpSecret != export.CheckedTotpSecret)
            {
                return PreparedChanges<bool>.NothingToSave(false);
            }

            if (!await identityChecking.TryPrepareBackupCodeUseAsync(export.BackupCodeId, export.IpAddress))
            {
                return PreparedChanges<bool>.NothingToSave(false);
            }

            accountRepository.PrepareUnchangedCheck(account);
        }

        securityJournal.PrepareEvent(SecurityEventType.DataExport, SecurityEventResult.Success, export.UserId, export.IpAddress);

        return PreparedChanges<bool>.SaveThenReturn(true);
    }

    // Note le refus dans le journal. Avec la double authentification, un mauvais mot de passe et un mauvais code reçoivent le même refus.
    private async Task<DataExportResult> RefuseAsync(User account, IPAddress? ipAddress)
    {
        await securityJournal.SaveEventNowAsync(SecurityEventType.DataExport, SecurityEventResult.Failure, account.Id, ipAddress);

        if (account.IsTwoFactorEnabled)
        {
            return new DataExportResult.WrongPasswordOrCode();
        }

        return new DataExportResult.WrongPassword();
    }

    // Ce qu'un passage doit enregistrer, et ce qui a été vérifié avant.
    private sealed class ExportToRecord
    {
        public required Guid UserId { get; init; }
        public required string CheckedPasswordHash { get; init; }
        public required string? CheckedTotpSecret { get; init; }
        public required Guid? BackupCodeId { get; init; }
        public required IPAddress? IpAddress { get; init; }
    }
}
