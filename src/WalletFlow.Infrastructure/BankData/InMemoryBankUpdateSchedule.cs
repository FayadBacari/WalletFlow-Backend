using WalletFlow.Application.BankConnections;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Infrastructure.BankData;

// Garde en mémoire l'heure de la dernière mise à jour en arrière-plan de chaque connexion : une toutes les 6 heures au plus.
// Après un redémarrage, chaque connexion est mise à jour au premier passage : si la banque refuse, rien ne change (GR Comptes).
public class InMemoryBankUpdateSchedule : IBankUpdateSchedule
{
    private readonly TimeProvider clock;
    private readonly Dictionary<Guid, DateTimeOffset> lastUpdateByConnection = new Dictionary<Guid, DateTimeOffset>();
    private readonly object scheduleLock = new object();

    // Reçoit l'horloge qui mesure les 6 heures.
    public InMemoryBankUpdateSchedule(TimeProvider clock)
    {
        this.clock = clock;
    }

    // Note l'heure et renvoie vrai si cette connexion n'a pas été mise à jour en arrière-plan depuis 6 heures.
    public bool TryStartBackgroundUpdate(Guid connectionId)
    {
        var now = clock.GetUtcNow();

        lock (scheduleLock)
        {
            RemoveFinishedIntervals(now);

            if (lastUpdateByConnection.ContainsKey(connectionId))
            {
                return false;
            }

            lastUpdateByConnection[connectionId] = now;
            return true;
        }
    }

    // Oublie les connexions dont les 6 heures sont passées, dont celles qui ont été supprimées depuis.
    private void RemoveFinishedIntervals(DateTimeOffset now)
    {
        var finishedConnections = lastUpdateByConnection
            .Where(entry => now - entry.Value >= BankConnectionRules.BackgroundUpdateInterval)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var connectionId in finishedConnections)
        {
            lastUpdateByConnection.Remove(connectionId);
        }
    }
}
