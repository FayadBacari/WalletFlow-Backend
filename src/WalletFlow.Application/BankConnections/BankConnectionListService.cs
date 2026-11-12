using System.Globalization;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.BankConnections;

// Cas d'utilisation : l'utilisateur connecté voit ses banques, ses comptes, leurs soldes et la date de chaque solde (GR Comptes).
public class BankConnectionListService
{
    // L'ordre alphabétique français, pour les banques et pour les comptes.
    private static readonly CompareInfo FrenchOrder = CultureInfo.GetCultureInfo("fr-FR").CompareInfo;

    private readonly IBankConnectionRepository connectionRepository;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux connexions et l'horloge.
    public BankConnectionListService(IBankConnectionRepository connectionRepository, TimeProvider clock)
    {
        this.connectionRepository = connectionRepository;
        this.clock = clock;
    }

    // Donne les connexions de l'utilisateur, triées par banque, chacune avec ses comptes triés par nom.
    public async Task<List<BankConnectionView>> ListAsync(Guid userId)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var connections = await connectionRepository.FindConnectionsAsync(userId);
        var accounts = await connectionRepository.FindAccountsAsync(connections.Select(connection => connection.Id).ToList());
        var views = new List<BankConnectionView>();

        connections.Sort((first, second) => FrenchOrder.Compare(first.BankName, second.BankName));

        foreach (var connection in connections)
        {
            var connectionAccounts = accounts.Where(account => account.BankConnectionId == connection.Id).ToList();
            connectionAccounts.Sort((first, second) => FrenchOrder.Compare(first.Name, second.Name));

            var status = connection.HasAuthorizationEnded(now) ? BankConnectionStatus.Expired : connection.Status;
            views.Add(new BankConnectionView(connection, status, connection.IsEndingSoon(now), connectionAccounts));
        }

        return views;
    }
}
