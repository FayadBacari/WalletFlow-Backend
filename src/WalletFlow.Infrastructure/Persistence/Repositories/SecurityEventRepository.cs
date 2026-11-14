using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Écrit les événements du journal de sécurité dans la base PostgreSQL (SS §11).
public class SecurityEventRepository : ISecurityEventRepository
{
    // Nom donné par PostgreSQL à la clé étrangère qui relie un événement à son compte.
    private const string AccountForeignKey = "evenement_securite_id_utilisateur_fkey";

    private readonly WalletFlowDatabaseContext databaseContext;

    // Reçoit l'accès à la base de la requête en cours.
    public SecurityEventRepository(WalletFlowDatabaseContext databaseContext)
    {
        this.databaseContext = databaseContext;
    }

    // Enregistre tout de suite un événement, seul : rien d'autre ne doit attendre l'unité de travail.
    // Si son compte vient d'être effacé, l'événement est gardé comme ceux d'un compte supprimé : sans identifiant ni adresse IP (GR Suppression).
    public async Task SaveEventAsync(SecurityEvent securityEvent)
    {
        databaseContext.ThrowIfChangesArePending();
        databaseContext.SecurityEvents.Add(securityEvent);

        try
        {
            await databaseContext.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            if (!PostgresErrors.IsForeignKeyViolation(exception, AccountForeignKey))
            {
                throw;
            }

            databaseContext.Entry(securityEvent).State = EntityState.Detached;
            databaseContext.SecurityEvents.Add(CreateEventOfErasedAccount(securityEvent));
            await databaseContext.SaveChangesAsync();
        }
    }

    // Prépare un événement : il partira avec les autres changements, dans la même transaction, à l'appel de l'unité de travail.
    public void PrepareEvent(SecurityEvent securityEvent)
    {
        databaseContext.SecurityEvents.Add(securityEvent);
    }

    // Appelle la fonction de la base qui efface les événements de plus d'un an : l'utilisateur de l'application n'a pas le droit
    // d'effacer le journal lui-même (MPD, section 8 du script). Cette écriture est immédiate, et n'emporte rien d'autre.
    public async Task<int> DeleteExpiredEventsAsync()
    {
        databaseContext.ThrowIfChangesArePending();

        return await databaseContext.Database
            .SqlQuery<int>($"SELECT fn_effacer_vieux_evenements() AS \"Value\"")
            .SingleAsync();
    }

    // Recrée l'événement sans identifiant de compte ni adresse IP. Le reste ne change pas : identifiant, type, résultat et date.
    private static SecurityEvent CreateEventOfErasedAccount(SecurityEvent securityEvent)
    {
        return new SecurityEvent
        {
            Id = securityEvent.Id,
            Type = securityEvent.Type,
            Result = securityEvent.Result,
            OccurredAt = securityEvent.OccurredAt
        };
    }
}
