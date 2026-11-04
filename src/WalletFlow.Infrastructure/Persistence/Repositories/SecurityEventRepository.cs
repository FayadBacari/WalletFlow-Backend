using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Écrit les événements du journal de sécurité dans la base PostgreSQL (SS §11).
public class SecurityEventRepository : ISecurityEventRepository
{
    private readonly WalletFlowDatabaseContext databaseContextOfTheCurrentHttpRequest;

    // Reçoit l'accès à la base de la requête en cours.
    public SecurityEventRepository(WalletFlowDatabaseContext databaseContextOfTheCurrentHttpRequest)
    {
        this.databaseContextOfTheCurrentHttpRequest = databaseContextOfTheCurrentHttpRequest;
    }

    // Enregistre un événement dans le journal de sécurité.
    public async Task SaveSecurityEventInTheJournalAsync(SecurityEvent securityEventToSaveInTheJournal)
    {
        databaseContextOfTheCurrentHttpRequest.SecurityEvents.Add(securityEventToSaveInTheJournal);

        await databaseContextOfTheCurrentHttpRequest.SaveChangesAsync();
    }
}
