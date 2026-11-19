namespace WalletFlow.Api.BackgroundTasks;

// Base des tâches de fond : un passage dès le démarrage, puis un passage après chaque attente, tant que l'API tourne. Chaque étape
// d'un passage a son propre accès à la base, oublié ensuite, et aucune étape ne commence après l'arrêt de l'API. Chaque tâche
// rattrape elle-même ses pannes et les écrit au journal technique, avec son propre message : une panne n'arrête jamais la tâche.
// Seule l'annulation demandée par l'arrêt remonte jusqu'ici (voir ShutdownCancellation). La base note aussi le résultat de chaque
// passage dans le bilan de santé des tâches : une étape qui échoue fait échouer son passage.
public abstract class RecurringBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly TimeProvider clock;
    private readonly BackgroundTaskHealth health;
    private int failedStepCount = 0;
    private int idlePass = 0;

    // Reçoit de quoi créer un accès à la base pour chaque étape, l'horloge, l'attente entre deux passages et le bilan de santé.
    protected RecurringBackgroundService(IServiceScopeFactory scopeFactory, TimeProvider clock, TimeSpan interval, BackgroundTaskHealth health)
    {
        this.scopeFactory = scopeFactory;
        this.clock = clock;
        this.health = health;
        Interval = interval;
    }

    // L'attente entre deux passages.
    public TimeSpan Interval { get; }

    // Le nom de la tâche dans le bilan de santé et le journal technique : celui de sa classe.
    public string Name => GetType().Name;

    // Fait un passage. Ses pannes y sont rattrapées ; seule l'annulation demandée par l'arrêt de l'API en sort.
    public abstract Task RunPassAsync(CancellationToken cancellationToken = default);

    // Tourne tant que l'API tourne (nom imposé par le framework). L'arrêt pendant l'attente termine la tâche normalement.
    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        health.Watch(Name, Interval, clock.GetUtcNow());

        while (!stoppingToken.IsCancellationRequested)
        {
            Interlocked.Exchange(ref failedStepCount, 0);
            Interlocked.Exchange(ref idlePass, 0);
            await RunPassAsync(stoppingToken);
            RecordPass();

            try
            {
                await WaitForNextPassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    // Attend le passage suivant, sur l'horloge de la tâche. Une tâche réveillée autrement remplace cette attente.
    protected virtual Task WaitForNextPassAsync(CancellationToken stoppingToken)
    {
        return Task.Delay(Interval, clock, stoppingToken);
    }

    // Donne à une étape son propre accès à la base, oublié ensuite. Après l'arrêt de l'API, aucune étape ne commence. Une étape qui
    // échoue fait échouer son passage dans le bilan de santé, avant que la tâche ne rattrape sa panne.
    protected async Task<TResult> RunInScopeAsync<TService, TResult>(Func<TService, Task<TResult>> step, CancellationToken cancellationToken = default)
        where TService : notnull
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<TService>();

            return await step(service);
        }
        catch (Exception exception) when (!ShutdownCancellation.IsShutdown(exception, cancellationToken))
        {
            ReportFailure();
            throw;
        }
    }

    // Fait échouer le passage en cours dans le bilan de santé, pour une panne qui ne lève aucune erreur, comme un e-mail qui n'a pas
    // pu partir.
    protected void ReportFailure()
    {
        Interlocked.Increment(ref failedStepCount);
    }

    // Dit que le passage en cours n'avait rien à faire, comme une file d'e-mails vide : il ne remet pas les échecs à zéro.
    protected void ReportIdlePass()
    {
        Interlocked.Exchange(ref idlePass, 1);
    }

    // Note le résultat du passage qui vient de finir : en échec dès qu'une étape a échoué, sinon à vide ou réussi.
    private void RecordPass()
    {
        var outcome = Volatile.Read(ref failedStepCount) > 0 ? BackgroundTaskHealth.PassOutcome.Failed
            : Volatile.Read(ref idlePass) == 1 ? BackgroundTaskHealth.PassOutcome.Idle
            : BackgroundTaskHealth.PassOutcome.Succeeded;

        health.RecordPass(Name, outcome, clock.GetUtcNow());
    }
}
