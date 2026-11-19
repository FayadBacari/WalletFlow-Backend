using WalletFlow.Application.AccountDeletion;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : toutes les heures, réessaie de révoquer les banques des comptes en suppression, puis efface les comptes dont
// la suppression est demandée depuis 30 jours (GR Suppression, SS §7, DD §12) : la révocation tentée juste avant est la dernière.
// Chaque étape a son propre accès à la base : celle qui échoue va au journal technique, sans arrêter les autres, et sera
// refaite au passage suivant. Le journal technique désigne le compte et la connexion par leur identifiant, jamais par une
// adresse (SS §7).
public class AccountDeletionBackgroundService : RecurringBackgroundService
{
    public static readonly TimeSpan PassInterval = TimeSpan.FromHours(1);

    private readonly ILogger<AccountDeletionBackgroundService> logger;

    // Reçoit de quoi créer un accès à la base pour chaque étape, l'horloge, le bilan de santé des tâches et le journal technique.
    public AccountDeletionBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        BackgroundTaskHealth health,
        ILogger<AccountDeletionBackgroundService> logger)
        : base(scopeFactory, clock, PassInterval, health)
    {
        this.logger = logger;
    }

    // Fait un passage : les révocations en retard, puis les effacements dus.
    public override async Task RunPassAsync(CancellationToken cancellationToken = default)
    {
        var revokedCount = await RetryRevocationsAsync(cancellationToken);
        var erasedCount = await EraseDueAccountsAsync(cancellationToken);

        logger.LogInformation(
            "{NombreDeRevocations} révocation(s) et {NombreDEffacements} effacement(s) de compte faits en arrière-plan.",
            revokedCount,
            erasedCount);
    }

    // Réessaie de révoquer chaque connexion pas encore révoquée d'un compte en suppression.
    private async Task<int> RetryRevocationsAsync(CancellationToken cancellationToken = default)
    {
        List<Guid> connectionIds;

        try
        {
            connectionIds = await RunInScopeAsync<DeletedAccountBankRevocation, List<Guid>>(revocation => revocation.FindConnectionsToRevokeAsync(cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
        {
            logger.LogError(exception, "La liste des connexions bancaires à révoquer ne peut pas être lue.");
            return 0;
        }

        var revokedCount = 0;

        foreach (var connectionId in connectionIds)
        {
            try
            {
                if (await RunInScopeAsync<DeletedAccountBankRevocation, bool>(revocation => revocation.TryRevokeAsync(connectionId, null, cancellationToken), cancellationToken))
                {
                    revokedCount = revokedCount + 1;
                }
            }
            catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
            {
                logger.LogError(exception, "La révocation de la connexion bancaire {IdentifiantConnexion} a échoué.", connectionId);
            }
        }

        return revokedCount;
    }

    // Efface chaque compte dont la suppression est demandée depuis 30 jours. Ses banques viennent d'avoir leur dernière tentative
    // de révocation, au début du passage : un prestataire qui ne répond toujours pas n'empêche pas l'effacement, l'autorisation
    // finira d'elle-même.
    private async Task<int> EraseDueAccountsAsync(CancellationToken cancellationToken = default)
    {
        List<Guid> userIds;

        try
        {
            userIds = await RunInScopeAsync<AccountErasureService, List<Guid>>(erasure => erasure.FindAccountsToEraseAsync(cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
        {
            logger.LogError(exception, "La liste des comptes à effacer ne peut pas être lue.");
            return 0;
        }

        var erasedCount = 0;

        foreach (var userId in userIds)
        {
            try
            {
                if (await RunInScopeAsync<AccountErasureService, bool>(erasure => erasure.EraseAsync(userId, cancellationToken), cancellationToken))
                {
                    erasedCount = erasedCount + 1;
                }
            }
            catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
            {
                logger.LogError(exception, "L'effacement du compte {IdentifiantUtilisateur} a échoué.", userId);
            }
        }

        return erasedCount;
    }
}
