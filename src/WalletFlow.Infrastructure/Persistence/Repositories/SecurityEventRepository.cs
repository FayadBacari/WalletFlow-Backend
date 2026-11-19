using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Écrit les événements du journal de sécurité dans la base PostgreSQL (SS §11).
public class SecurityEventRepository : ISecurityEventRepository
{
    private readonly WalletFlowDatabaseContext databaseContext;

    // Reçoit l'accès à la base de la requête en cours.
    public SecurityEventRepository(WalletFlowDatabaseContext databaseContext)
    {
        this.databaseContext = databaseContext;
    }

    // Enregistre tout de suite un événement, seul : rien d'autre ne doit attendre l'unité de travail.
    // Si son compte vient d'être effacé, l'événement est gardé comme ceux d'un compte supprimé : sans identifiant ni adresse IP (GR Suppression).
    // Si le compte qu'une action de l'administrateur vise vient d'être effacé, l'action est gardée sans lui (DD §4.5).
    // L'écriture ne s'annule jamais, même si le client abandonne sa requête : sinon, il suffirait de raccrocher pour ne laisser
    // aucune trace dans le journal.
    public async Task SaveEventAsync(SecurityEvent securityEvent)
    {
        databaseContext.ThrowIfChangesArePending();
        databaseContext.SecurityEvents.Add(securityEvent);

        try
        {
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException exception)
        {
            SecurityEvent keptEvent;

            if (PostgresErrors.IsForeignKeyViolation(exception, PostgresErrors.SecurityEventAccountForeignKey))
            {
                keptEvent = CreateEventOfErasedAccount(securityEvent);
            }
            else if (PostgresErrors.IsForeignKeyViolation(exception, PostgresErrors.SecurityEventTargetForeignKey))
            {
                keptEvent = CreateEventOfErasedTarget(securityEvent);
            }
            else
            {
                throw;
            }

            databaseContext.Entry(securityEvent).State = EntityState.Detached;
            databaseContext.SecurityEvents.Add(keptEvent);
            await databaseContext.SaveChangesAsync(CancellationToken.None);
        }
    }

    // Lit la date du dernier événement réussi de ce type pour ce compte. Le journal garde un an d'événements (SS §11).
    public async Task<DateTime?> FindLastSuccessAsync(SecurityEventType type, Guid accountId, CancellationToken cancellationToken = default)
    {
        return await databaseContext.SecurityEvents.AsNoTracking()
            .Where(securityEvent =>
                securityEvent.UserId == accountId
                && securityEvent.Type == type
                && securityEvent.Result == SecurityEventResult.Success)
            .MaxAsync(securityEvent => (DateTime?)securityEvent.OccurredAt, cancellationToken);
    }

    // Prépare un événement : il partira avec les autres changements, dans la même transaction, à l'appel de l'unité de travail.
    public void PrepareEvent(SecurityEvent securityEvent)
    {
        databaseContext.SecurityEvents.Add(securityEvent);
    }

    // Appelle la fonction de la base qui efface les événements de plus d'un an : l'utilisateur de l'application n'a pas le droit
    // d'effacer le journal lui-même (MPD, section 8 du script). Cette écriture est immédiate, et n'emporte rien d'autre.
    public async Task<int> DeleteExpiredEventsAsync(CancellationToken cancellationToken = default)
    {
        databaseContext.ThrowIfChangesArePending();

        return await databaseContext.Database
            .SqlQuery<int>($"SELECT fn_effacer_vieux_evenements() AS \"Value\"")
            .SingleAsync(cancellationToken);
    }

    // Recrée l'événement sans identifiant de compte ni adresse IP. Le reste ne change pas : identifiant, type, résultat et date.
    // Le compte visé reste : seul le compte de l'événement a été effacé.
    private static SecurityEvent CreateEventOfErasedAccount(SecurityEvent securityEvent)
    {
        return new SecurityEvent
        {
            Id = securityEvent.Id,
            Type = securityEvent.Type,
            Result = securityEvent.Result,
            OccurredAt = securityEvent.OccurredAt,
            TargetUserId = securityEvent.TargetUserId
        };
    }

    // Recrée l'action de l'administrateur sans le compte qu'elle visait. Le reste ne change pas.
    private static SecurityEvent CreateEventOfErasedTarget(SecurityEvent securityEvent)
    {
        return new SecurityEvent
        {
            Id = securityEvent.Id,
            Type = securityEvent.Type,
            Result = securityEvent.Result,
            OccurredAt = securityEvent.OccurredAt,
            IpAddress = securityEvent.IpAddress,
            UserId = securityEvent.UserId
        };
    }
}
