using WalletFlow.Infrastructure.Persistence;

namespace WalletFlow.Api.BackgroundTasks;

// Garantit qu'une seule instance de l'API tourne sur une base. Ses limites, ses connexions qui attendent leur code et ses
// autorisations bancaires en cours sont gardées en mémoire, et ses tâches de fond ne doivent jamais tourner deux fois. Branchée
// avant toutes les tâches de fond et avant le serveur, la garde prend le verrou de l'instance au démarrage : une deuxième instance
// refuse de démarrer. Ensuite, elle vérifie le verrou à intervalles réguliers. Perdu avec sa connexion, il est repris ; pris
// entre-temps par une autre instance, c'est celle-ci qui s'arrête. Entre deux contrôles, les deux instances peuvent tourner
// ensemble, au plus un intervalle.
public sealed class SingleInstanceGuard : IHostedService, IDisposable
{
    public static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromSeconds(10);
    public const string AnotherInstanceMessage = "Une autre instance de l'API tourne déjà sur cette base : WalletFlow n'en accepte qu'une.";

    private readonly SingleInstanceLock instanceLock;
    private readonly IHostApplicationLifetime lifetime;
    private readonly ILogger<SingleInstanceGuard> logger;
    private readonly TimeSpan checkInterval;
    private readonly CancellationTokenSource stopping = new CancellationTokenSource();
    private Task watching = Task.CompletedTask;

    // Reçoit le verrou de l'instance, la vie de l'API, qu'elle peut arrêter, et le journal technique.
    public SingleInstanceGuard(SingleInstanceLock instanceLock, IHostApplicationLifetime lifetime, ILogger<SingleInstanceGuard> logger)
        : this(instanceLock, lifetime, logger, DefaultCheckInterval)
    {
    }

    // Reçoit aussi l'intervalle des contrôles : un test le raccourcit.
    public SingleInstanceGuard(
        SingleInstanceLock instanceLock,
        IHostApplicationLifetime lifetime,
        ILogger<SingleInstanceGuard> logger,
        TimeSpan checkInterval)
    {
        this.instanceLock = instanceLock;
        this.lifetime = lifetime;
        this.logger = logger;
        this.checkInterval = checkInterval;
    }

    // Prend le verrou avant que rien d'autre ne démarre, ou refuse le démarrage (nom imposé par le framework).
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!await instanceLock.TryAcquireAsync(cancellationToken))
        {
            throw new InvalidOperationException(AnotherInstanceMessage);
        }

        watching = WatchAsync(stopping.Token);
    }

    // Arrête les contrôles, puis rend le verrou (nom imposé par le framework).
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await stopping.CancelAsync();
        await watching;
        await instanceLock.ReleaseAsync();
    }

    // Vérifie le verrou à chaque intervalle, jusqu'à l'arrêt de l'API.
    private async Task WatchAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            bool keepsTheLock;

            try
            {
                keepsTheLock = await CheckAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (!keepsTheLock)
            {
                lifetime.StopApplication();
                return;
            }
        }
    }

    // Fait un contrôle. Renvoie faux seulement si une autre instance tient le verrou : celle-ci doit s'arrêter. La session perdue de
    // cette instance, ou une base qui ne répond pas, n'arrête rien : le verrou sera repris à un contrôle suivant.
    private async Task<bool> CheckAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (await instanceLock.IsHeldAsync(stoppingToken))
            {
                return true;
            }

            if (await instanceLock.TryAcquireAsync(stoppingToken))
            {
                logger.LogWarning("Le verrou de l'instance unique était perdu avec sa connexion : il est repris.");
                return true;
            }

            // La session perdue peut tenir le verrou encore un instant : il sera repris au contrôle suivant.
            var holder = await instanceLock.FindHolderAsync(stoppingToken);

            if (holder == null || holder == instanceLock.LastProcessId)
            {
                logger.LogWarning("Le verrou de l'instance unique n'a pas pu être repris : nouvel essai au contrôle suivant.");
                return true;
            }

            logger.LogCritical("Une autre instance de l'API a pris le verrou de l'instance unique : celle-ci s'arrête.");
            return false;
        }
        catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, stoppingToken))
        {
            logger.LogError(exception, "Le verrou de l'instance unique ne peut pas être vérifié : la base ne répond pas.");
            return true;
        }
    }

    // Libère l'annulation des contrôles (nom imposé par .NET).
    public void Dispose()
    {
        stopping.Dispose();
    }
}
