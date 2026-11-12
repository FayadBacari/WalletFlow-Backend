namespace WalletFlow.Application.BankConnections;

// Le prestataire agréé qui lit les comptes bancaires, en lecture seule (GR Comptes, SS §7). Implémenté dans la couche Infrastructure.
// Chaque refus ou panne du prestataire arrive comme une BankProviderException, qui dit sa raison.
public interface IBankDataProvider
{
    // Donne les banques françaises que le prestataire sait lire.
    Task<List<ProvidedBank>> ListBanksAsync();

    // Commence une autorisation auprès de cette banque, et renvoie l'adresse de sa page, où l'utilisateur s'identifie (GR Comptes).
    Task<string> StartAuthorizationAsync(BankAuthorizationRequest request);

    // Termine l'autorisation avec le code renvoyé par la banque, et donne la nouvelle session et ses comptes.
    Task<ProvidedSession> CreateSessionAsync(string code);

    // Donne les comptes d'une session encore autorisée. Une session finie lève une erreur SessionEnded.
    Task<List<SessionAccount>> ReadSessionAccountsAsync(string sessionId);

    // Donne les soldes d'un compte. Quand l'utilisateur est là, la lecture ne compte pas dans les 4 lectures par jour de la banque.
    Task<List<ProvidedBalance>> ReadBalancesAsync(string accountUid, UserPresence? presence);

    // Révoque la session chez le prestataire. Une session déjà finie, ou inconnue, compte comme révoquée.
    Task RevokeSessionAsync(string sessionId);
}
