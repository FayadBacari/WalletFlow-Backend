using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde le journal de sécurité en mémoire.
public class FakeSecurityEventRepository : ISecurityEventRepository
{
    public const string TechnicalDetailOfTheSimulatedJournalFailure = "detail-technique-de-la-panne-du-journal";

    public List<SecurityEvent> SavedSecurityEvents { get; } = new List<SecurityEvent>();

    // Simule une panne de la base au moment d'écrire dans le journal.
    public bool DatabaseIsDownDuringThisTest { get; set; } = false;

    // Garde l'événement en mémoire.
    public Task SaveSecurityEventInTheJournalAsync(SecurityEvent securityEventToSaveInTheJournal)
    {
        if (DatabaseIsDownDuringThisTest)
        {
            throw new InvalidOperationException(TechnicalDetailOfTheSimulatedJournalFailure);
        }

        SavedSecurityEvents.Add(securityEventToSaveInTheJournal);
        return Task.CompletedTask;
    }
}
