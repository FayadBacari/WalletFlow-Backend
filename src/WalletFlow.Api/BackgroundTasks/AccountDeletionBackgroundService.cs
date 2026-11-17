using WalletFlow.Application.AccountDeletion;

namespace WalletFlow.Api.BackgroundTasks;

// Tâche de fond : toutes les heures, réessaie de révoquer les banques des comptes en suppression, puis efface les comptes dont
// la suppression est demandée depuis 30 jours (GR Suppression, SS §7, DD §12) : la révocation tentée juste avant est la dernière.
// Chaque étape a son propre accès à la base : celle qui échoue va au journal technique, sans arrêter les autres, et sera
// refaite au passage suivant. Le journal technique désigne le compte et la connexion par leur identifiant, jamais par une
// adresse (SS §7).
public class AccountDeletionBackgroundService : BackgroundService
{
    public static readonly TimeSpan PassInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory scopeFactory;
    private readonly TimeProvider clock;
    private readonly ILogger<AccountDeletionBackgroundService> logger;

    // Reçoit de quoi créer un accès à la base pour chaque étape, l'horloge et le journal technique.
    public AccountDeletionBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<AccountDeletionBackgroundService> logger)
    {
        this.scopeFactory = scopeFactory;
        this.clock = clock;
        this.logger = logger;
    }

    // Tourne tant que l'API tourne (nom imposé par le framework).
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunPassAsync();
            await Task.Delay(PassInterval, clock, stoppingToken);
        }
    }

    // Fait un passage : les révocations en retard, puis les effacements dus.
    public async Task RunPassAsync()
    {
        var revokedCount = await RetryRevocationsAsync();
        var erasedCount = await EraseDueAccountsAsync();

        logger.LogInformation(
            "{NombreDeRevocations} révocation(s) et {NombreDEffacements} effacement(s) de compte faits en arrière-plan.",
            revokedCount,
            erasedCount);
    }

    // Réessaie de révoquer chaque connexion pas encore révoquée d'un compte en suppression.
    private async Task<int> RetryRevocationsAsync()
    {
        List<Guid> connectionIds;

        try
        {
            connectionIds = await RunInScopeAsync<DeletedAccountBankRevocation, List<Guid>>(revocation => revocation.FindConnectionsToRevokeAsync());
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "La liste des connexions bancaires à révoquer ne peut pas être lue.");
            return 0;
        }

        var revokedCount = 0;

        foreach (var connectionId in connectionIds)
        {
            try
            {
                if (await RunInScopeAsync<DeletedAccountBankRevocation, bool>(revocation => revocation.TryRevokeAsync(connectionId, null)))
                {
                    revokedCount = revokedCount + 1;
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "La révocation de la connexion bancaire {IdentifiantConnexion} a échoué.", connectionId);
            }
        }

        return revokedCount;
    }

    // Efface chaque compte dont la suppression est demandée depuis 30 jours. Ses banques viennent d'avoir leur dernière tentative
    // de révocation, au début du passage : un prestataire qui ne répond toujours pas n'empêche pas l'effacement, l'autorisation
    // finira d'elle-même.
    private async Task<int> EraseDueAccountsAsync()
    {
        List<Guid> userIds;

        try
        {
            userIds = await RunInScopeAsync<AccountErasureService, List<Guid>>(erasure => erasure.FindAccountsToEraseAsync());
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "La liste des comptes à effacer ne peut pas être lue.");
            return 0;
        }

        var erasedCount = 0;

        foreach (var userId in userIds)
        {
            try
            {
                if (await RunInScopeAsync<AccountErasureService, bool>(erasure => erasure.EraseAsync(userId)))
                {
                    erasedCount = erasedCount + 1;
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "L'effacement du compte {IdentifiantUtilisateur} a échoué.", userId);
            }
        }

        return erasedCount;
    }

    // Donne à chaque étape son propre accès à la base, oublié ensuite.
    private async Task<TResult> RunInScopeAsync<TService, TResult>(Func<TService, Task<TResult>> step)
        where TService : notnull
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<TService>();

        return await step(service);
    }
}
