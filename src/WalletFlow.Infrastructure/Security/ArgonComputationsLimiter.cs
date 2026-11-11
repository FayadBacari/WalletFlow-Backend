namespace WalletFlow.Infrastructure.Security;

// Limite le nombre de calculs Argon2id faits en même temps : chacun prend 32 Mio de mémoire, et un afflux de requêtes
// pourrait sinon remplir la mémoire du serveur. Au-delà de la limite, un calcul attend son tour sans bloquer de fil d'exécution.
// Un seul limiteur sert à tous les hachages du serveur : mots de passe et codes de secours.
public class ArgonComputationsLimiter
{
    private readonly SemaphoreSlim slots;

    // Reçoit le nombre de calculs permis en même temps : au moins un.
    public ArgonComputationsLimiter(int maximumConcurrentComputations)
    {
        if (maximumConcurrentComputations < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentComputations), "Il faut permettre au moins un calcul à la fois.");
        }

        slots = new SemaphoreSlim(maximumConcurrentComputations, maximumConcurrentComputations);
    }

    // Lance le calcul dès qu'une place est libre, puis libère la place, même si le calcul échoue.
    public async Task<TResult> RunAsync<TResult>(Func<TResult> computation)
    {
        await slots.WaitAsync();

        try
        {
            return computation();
        }
        finally
        {
            slots.Release();
        }
    }
}
