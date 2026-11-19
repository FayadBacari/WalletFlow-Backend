using WalletFlow.Application.Persistence;
using WalletFlow.Application.UserAccounts;

namespace WalletFlow.Application.TwoFactorAuthentication;

// Cas d'utilisation : rechiffre avec la clé actuelle les secrets de double authentification chiffrés avec une ancienne clé
// (SS §7). Une fois tous rechiffrés, l'ancienne clé peut quitter la configuration : volée, elle ne déchiffre plus rien.
public class TotpSecretReEncryptionService
{
    public const int AccountBatchSize = 100;

    private readonly IUserAccountRepository accountRepository;
    private readonly DatabaseChangesSavingService savingService;
    private readonly ITotpSecretProtector secretProtector;

    // Reçoit l'accès aux comptes, l'enregistrement des changements et le chiffrement des secrets.
    public TotpSecretReEncryptionService(
        IUserAccountRepository accountRepository,
        DatabaseChangesSavingService savingService,
        ITotpSecretProtector secretProtector)
    {
        this.accountRepository = accountRepository;
        this.savingService = savingService;
        this.secretProtector = secretProtector;
    }

    // Donne le paquet suivant de comptes dont le secret attend la clé actuelle, après celui-ci s'il est donné.
    public async Task<List<Guid>> FindAccountsToReEncryptAsync(Guid? afterUserId, CancellationToken cancellationToken = default)
    {
        return await accountRepository.FindAccountsWithOtherTotpKeyAsync(secretProtector.CurrentKeyPrefix, afterUserId, AccountBatchSize, cancellationToken);
    }

    // Rechiffre le secret de ce compte avec la clé actuelle. Renvoie faux s'il n'y a plus rien à faire : compte effacé, double
    // authentification désactivée, ou secret déjà rechiffré par une autre requête. Un secret qu'aucune clé configurée ne
    // déchiffre lève une erreur : il n'est jamais remplacé par autre chose.
    public async Task<bool> ReEncryptAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await savingService.SaveWithRetriesAsync(() => PrepareReEncryptionAsync(userId, cancellationToken), false, cancellationToken);
    }

    // Un passage : relit le compte, déchiffre son secret avec sa clé, et prépare le même secret chiffré avec la clé actuelle.
    // L'enregistrement échoue si une autre requête a changé le compte entre-temps : le passage suivant relit tout.
    private async Task<PreparedChanges<bool>> PrepareReEncryptionAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.FindByIdAsync(userId, cancellationToken);
        if (account?.EncryptedTotpSecret is not string encryptedSecret || secretProtector.UsesCurrentKey(encryptedSecret))
        {
            return PreparedChanges<bool>.NothingToSave(false);
        }

        var secret = secretProtector.Decrypt(encryptedSecret, userId);
        account.ReplaceEncryptedTotpSecret(secretProtector.Encrypt(secret, userId));

        return PreparedChanges<bool>.SaveThenReturn(true);
    }
}
