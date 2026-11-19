namespace WalletFlow.Application.Persistence;

// Dit si la base répond. Le point de santé de l'API s'en sert pour dire si elle est prête à servir des requêtes.
public interface IDatabaseHealth
{
    // Vrai si une connexion à la base s'ouvre et répond.
    Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);
}
