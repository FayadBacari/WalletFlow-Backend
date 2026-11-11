using WalletFlow.Application.AccountRecovery;

namespace WalletFlow.Tests.Fakes;

// Remplace la file des e-mails dans les tests des services : garde les demandes en mémoire, sans les traiter.
public class FakeBackgroundEmailRequestQueue : IBackgroundEmailRequestQueue
{
    public List<BackgroundEmailRequest> AddedRequests { get; } = new List<BackgroundEmailRequest>();

    // Garde la demande en mémoire, pour que le test puisse la relire.
    public void Add(BackgroundEmailRequest request)
    {
        AddedRequests.Add(request);
    }

    // Les tests des services ne lisent jamais la file : c'est le rôle de la tâche de fond, testée avec la vraie file.
    public ValueTask<BackgroundEmailRequest> WaitForNextAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Cette fausse file ne se lit pas.");
    }
}
