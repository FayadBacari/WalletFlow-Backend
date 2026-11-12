using WalletFlow.Application.BankConnections;

namespace WalletFlow.Infrastructure.BankData;

// Remplace le prestataire bancaire en développement, tant que l'application Enable Banking n'est pas configurée :
// chaque appel répond que le prestataire ne répond pas, et les routes des banques répondent 503.
// En production, l'API refuse de démarrer sans ses réglages (voir Program.cs).
public class BankProviderNotConfigured : IBankDataProvider
{
    // Toutes les méthodes échouent de la même façon.
    private static BankProviderException NotConfigured()
    {
        return new BankProviderException(BankProviderFailure.Unavailable, "Enable Banking n'est pas configuré.");
    }

    // Échoue : aucun prestataire n'est configuré.
    public Task<List<ProvidedBank>> ListBanksAsync()
    {
        throw NotConfigured();
    }

    // Échoue : aucun prestataire n'est configuré.
    public Task<string> StartAuthorizationAsync(BankAuthorizationRequest request)
    {
        throw NotConfigured();
    }

    // Échoue : aucun prestataire n'est configuré.
    public Task<ProvidedSession> CreateSessionAsync(string code)
    {
        throw NotConfigured();
    }

    // Échoue : aucun prestataire n'est configuré.
    public Task<List<SessionAccount>> ReadSessionAccountsAsync(string sessionId)
    {
        throw NotConfigured();
    }

    // Échoue : aucun prestataire n'est configuré.
    public Task<List<ProvidedBalance>> ReadBalancesAsync(string accountUid, UserPresence? presence)
    {
        throw NotConfigured();
    }

    // Échoue : aucun prestataire n'est configuré.
    public Task RevokeSessionAsync(string sessionId)
    {
        throw NotConfigured();
    }
}
