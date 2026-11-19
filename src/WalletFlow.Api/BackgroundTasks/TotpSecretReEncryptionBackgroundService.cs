using WalletFlow.Application.TwoFactorAuthentication;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : dès le démarrage, puis chaque jour, rechiffre avec la clé actuelle les secrets de double authentification
// chiffrés avec une ancienne clé (SS §7). Chaque compte a son propre accès à la base : un échec va au journal technique, sans
// arrêter les autres, et sera refait au passage suivant. Tant que le journal technique compte des échecs, l'ancienne clé doit
// rester dans la configuration. Le journal désigne le compte par son identifiant, jamais par son adresse (SS §7).
public class TotpSecretReEncryptionBackgroundService : RecurringBackgroundService
{
    public static readonly TimeSpan PassInterval = TimeSpan.FromDays(1);

    private readonly ILogger<TotpSecretReEncryptionBackgroundService> logger;

    // Reçoit de quoi créer un accès à la base pour chaque compte, l'horloge, le bilan de santé des tâches et le journal technique.
    public TotpSecretReEncryptionBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        BackgroundTaskHealth health,
        ILogger<TotpSecretReEncryptionBackgroundService> logger)
        : base(scopeFactory, clock, PassInterval, health)
    {
        this.logger = logger;
    }

    // Fait un passage : parcourt par paquets les comptes dont le secret attend la clé actuelle, dans l'ordre de leur identifiant.
    public override async Task RunPassAsync(CancellationToken cancellationToken = default)
    {
        Guid? lastUserId = null;
        var reEncryptedCount = 0;
        var failedCount = 0;

        while (true)
        {
            List<Guid> userIds;

            try
            {
                userIds = await RunInScopeAsync<TotpSecretReEncryptionService, List<Guid>>(service => service.FindAccountsToReEncryptAsync(lastUserId, cancellationToken), cancellationToken);
            }
            catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
            {
                logger.LogError(exception, "La liste des secrets de double authentification à rechiffrer ne peut pas être lue.");
                return;
            }

            if (userIds.Count == 0)
            {
                break;
            }

            foreach (var userId in userIds)
            {
                try
                {
                    if (await RunInScopeAsync<TotpSecretReEncryptionService, bool>(service => service.ReEncryptAsync(userId, cancellationToken), cancellationToken))
                    {
                        reEncryptedCount = reEncryptedCount + 1;
                    }
                }
                catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
                {
                    failedCount = failedCount + 1;
                    logger.LogError(exception, "Le secret de double authentification du compte {IdentifiantUtilisateur} n'a pas pu être rechiffré.", userId);
                }
            }

            lastUserId = userIds[userIds.Count - 1];
        }

        logger.LogInformation(
            "{NombreDeSecrets} secret(s) de double authentification rechiffré(s) avec la clé actuelle, {NombreDEchecs} échec(s).",
            reEncryptedCount,
            failedCount);
    }
}
