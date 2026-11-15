using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Emails;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Accès à la file d'e-mails, avec EF Core. Une demande qui entre dans la file réveille la tâche de fond des e-mails,
// mais seulement une fois enregistrée : la tâche ne cherche donc jamais une demande qui n'est pas encore en base.
public class EmailRequestRepository : IEmailRequestRepository
{
    private readonly WalletFlowDatabaseContext databaseContext;
    private readonly EmailRequestWakeUp wakeUp;
    private bool savesNewRequests = false;

    // Reçoit l'accès à la base de la requête en cours, et le réveil de la tâche de fond.
    public EmailRequestRepository(WalletFlowDatabaseContext databaseContext, EmailRequestWakeUp wakeUp)
    {
        this.databaseContext = databaseContext;
        this.wakeUp = wakeUp;

        databaseContext.SavingChanges += NoteNewRequests;
        databaseContext.SavedChanges += WakeUpAfterSaving;
    }

    // Prépare l'entrée d'une demande dans la file.
    public void PrepareRequest(EmailRequest request)
    {
        databaseContext.EmailRequests.Add(request);
    }

    // Donne la demande dont l'heure d'essai est passée depuis le plus longtemps (index ix_demande_email_essai).
    public async Task<Guid?> FindNextDueIdAsync(DateTime now)
    {
        return await databaseContext.EmailRequests
            .Where(request => request.NextTryAt <= now)
            .OrderBy(request => request.NextTryAt)
            .Select(request => (Guid?)request.Id)
            .FirstOrDefaultAsync();
    }

    // Cherche une demande par son identifiant. La demande lue est suivie.
    public async Task<EmailRequest?> FindAsync(Guid requestId)
    {
        return await databaseContext.EmailRequests.SingleOrDefaultAsync(request => request.Id == requestId);
    }

    // Prépare la sortie d'une demande de la file.
    public void PrepareRemoval(EmailRequest request)
    {
        databaseContext.EmailRequests.Remove(request);
    }

    // Juste avant un enregistrement : note s'il fait entrer des demandes dans la file.
    private void NoteNewRequests(object? sender, SavingChangesEventArgs eventArgs)
    {
        savesNewRequests = databaseContext.ChangeTracker.Entries<EmailRequest>()
            .Any(entry => entry.State == EntityState.Added);
    }

    // Juste après un enregistrement réussi : réveille la tâche de fond si des demandes viennent d'entrer dans la file.
    private void WakeUpAfterSaving(object? sender, SavedChangesEventArgs eventArgs)
    {
        if (savesNewRequests)
        {
            savesNewRequests = false;
            wakeUp.Signal();
        }
    }
}
