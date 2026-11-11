namespace WalletFlow.Application.AccountRecovery;

// File des e-mails à préparer en arrière-plan : la route répond tout de suite,
// et le temps de réponse ne révèle donc pas si un compte existe (SS §2). Implémentée dans la couche Infrastructure.
public interface IBackgroundEmailRequestQueue
{
    // Ajoute une demande à la file. Si la file est pleine, la demande est perdue et la panne va au journal technique.
    void Add(BackgroundEmailRequest request);

    // Attend la prochaine demande de la file.
    ValueTask<BackgroundEmailRequest> WaitForNextAsync(CancellationToken cancellationToken);
}
