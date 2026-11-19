using WalletFlow.Application.Security;

namespace WalletFlow.Infrastructure.Security;

// Limite le nombre de calculs Argon2id faits en même temps : chacun prend 32 Mio de mémoire, et un afflux de requêtes
// pourrait sinon remplir la mémoire du serveur. Au-delà de la limite, un calcul attend son tour sans bloquer de fil d'exécution.
// Un seul limiteur sert à tous les hachages du serveur : mots de passe et codes de secours.
// La file d'attente est bornée, et l'attente aussi : sous une rafale de connexions, une requête de trop reçoit tout de suite
// « serveur occupé » au lieu de s'empiler. Une requête abandonnée par son client quitte la file, et son calcul ne part jamais.
public sealed class ArgonComputationsLimiter : IDisposable
{
    // Au plus ce nombre de calculs attendent leur tour pour chaque calcul permis en même temps.
    public const int WaitingPerSlot = 4;

    // Au plus ce temps d'attente avant le calcul : au-delà, la requête reçoit « serveur occupé ».
    public static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim slots;
    private readonly int maximumWaiting;
    private readonly TimeSpan maximumWait;
    private int waitingCount = 0;

    // Reçoit le nombre de calculs permis en même temps : au moins un. L'attente maximale ne change que dans les tests.
    public ArgonComputationsLimiter(int maximumConcurrentComputations, TimeSpan? maximumWait = null)
    {
        if (maximumConcurrentComputations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentComputations), "Il faut permettre au moins un calcul à la fois.");
        }

        slots = new SemaphoreSlim(maximumConcurrentComputations, maximumConcurrentComputations);
        maximumWaiting = maximumConcurrentComputations * WaitingPerSlot;
        this.maximumWait = maximumWait ?? MaximumWait;
    }

    // Le nombre de calculs qui attendent leur tour en ce moment.
    public int WaitingCount => Volatile.Read(ref waitingCount);

    // Lance le calcul dès qu'une place est libre, puis libère la place, même si le calcul échoue. Refuse tout de suite si la file
    // est pleine, et après l'attente maximale si aucune place ne s'est libérée.
    public async Task<TResult> RunAsync<TResult>(Func<TResult> computation, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref waitingCount) > maximumWaiting)
        {
            Interlocked.Decrement(ref waitingCount);
            throw new ServerBusyException();
        }

        bool hasSlot;

        try
        {
            hasSlot = await slots.WaitAsync(maximumWait, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref waitingCount);
        }

        if (!hasSlot)
        {
            throw new ServerBusyException();
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return computation();
        }
        finally
        {
            slots.Release();
        }
    }

    // Libère les places quand l'API s'arrête : le conteneur de services, qui a créé ce limiteur, appelle cette méthode.
    public void Dispose()
    {
        slots.Dispose();
    }
}
