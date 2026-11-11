using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using WalletFlow.Application.AccountRecovery;

namespace WalletFlow.Infrastructure.Emails;

// Garde en mémoire les e-mails à préparer en arrière-plan. Les demandes en attente sont perdues si l'API redémarre :
// le visiteur n'a plus qu'à redemander. La taille est limitée, pour que la mémoire ne puisse pas grossir sans fin.
public class InMemoryBackgroundEmailRequestQueue : IBackgroundEmailRequestQueue
{
    public const int QueueCapacity = 1000;

    private readonly Channel<BackgroundEmailRequest> channel = Channel.CreateBounded<BackgroundEmailRequest>(QueueCapacity);
    private readonly ILogger<InMemoryBackgroundEmailRequestQueue> logger;

    // Reçoit le journal technique.
    public InMemoryBackgroundEmailRequestQueue(ILogger<InMemoryBackgroundEmailRequestQueue> logger)
    {
        this.logger = logger;
    }

    // Ajoute une demande à la file. Si la file est pleine, la demande est perdue et la panne va au journal technique, sans l'adresse (SS §7).
    public void Add(BackgroundEmailRequest request)
    {
        if (!channel.Writer.TryWrite(request))
        {
            logger.LogError(
                "La file des e-mails est pleine : une demande de type {TypeDeDemande} est perdue.",
                request.Type);
        }
    }

    // Attend la prochaine demande de la file.
    public ValueTask<BackgroundEmailRequest> WaitForNextAsync(CancellationToken cancellationToken)
    {
        return channel.Reader.ReadAsync(cancellationToken);
    }
}
