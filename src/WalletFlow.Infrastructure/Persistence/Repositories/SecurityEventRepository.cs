using Microsoft.EntityFrameworkCore;
using Npgsql;
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
            if (!IsAccountForeignKeyViolation(exception))
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

    // Renvoie vrai si l'erreur vient de la clé étrangère du compte : le compte de l'événement n'existe plus.
    private static bool IsAccountForeignKeyViolation(DbUpdateException exception)
    {
        if (exception.InnerException is not PostgresException postgresException)
        {
            return false;
        }

        return postgresException.SqlState == PostgresErrorCodes.ForeignKeyViolation
            && postgresException.ConstraintName == AccountForeignKey;
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
