using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Security;

// Écriture dans le journal de sécurité (SS §11). Implémenté dans la couche Infrastructure.
public interface ISecurityEventRepository
{
    // Enregistre un événement dans le journal de sécurité.
    Task SaveSecurityEventInTheJournalAsync(SecurityEvent securityEventToSaveInTheJournal);
}
