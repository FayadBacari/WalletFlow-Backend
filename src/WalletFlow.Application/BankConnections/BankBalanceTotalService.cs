namespace WalletFlow.Application.BankConnections;

// Cas d'utilisation : l'utilisateur connecté voit son solde total, tous comptes confondus (GR Comptes). Il est calculé
// à chaque fois, jamais enregistré (DD §11). Chaque compte compte avec son dernier solde connu, même quand sa banque
// ne répond pas ou que son autorisation a expiré : la date du plus ancien solde dit à l'utilisateur de quand date le total.
public class BankBalanceTotalService
{
    private readonly IBankConnectionRepository connectionRepository;

    // Reçoit l'accès aux connexions et à leurs comptes.
    public BankBalanceTotalService(IBankConnectionRepository connectionRepository)
    {
        this.connectionRepository = connectionRepository;
    }

    // Additionne les derniers soldes connus de tous les comptes de l'utilisateur. Le total tient dans un long :
    // il ne déborde jamais, même avec beaucoup de comptes au solde maximum.
    public async Task<BankBalanceTotal> ReadAsync(Guid userId)
    {
        var connections = await connectionRepository.FindConnectionsAsync(userId);
        var accounts = await connectionRepository.FindAccountsAsync(connections.Select(connection => connection.Id).ToList());
        long totalInCents = 0;
        DateTime? oldestBalanceUpdatedAt = null;

        foreach (var account in accounts)
        {
            totalInCents = totalInCents + account.BalanceInCents;

            if (oldestBalanceUpdatedAt == null || account.BalanceUpdatedAt < oldestBalanceUpdatedAt)
            {
                oldestBalanceUpdatedAt = account.BalanceUpdatedAt;
            }
        }

        return new BankBalanceTotal(totalInCents, oldestBalanceUpdatedAt, accounts.Count);
    }
}
