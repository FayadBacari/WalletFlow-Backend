using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Administration;
using WalletFlow.Application.Support;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Lit et écrit, pour l'administrateur, les demandes de support et les statistiques dans la base PostgreSQL (GR Admin). Ces
// lectures ne filtrent sur aucun utilisateur : seul un administrateur y arrive. Elles ne touchent jamais aux comptes bancaires,
// aux opérations, aux soldes ni aux sacs (SS §8).
public class AdministrationRepository : IAdministrationRepository
{
    private readonly WalletFlowDatabaseContext databaseContext;

    // Reçoit l'accès à la base de la requête en cours.
    public AdministrationRepository(WalletFlowDatabaseContext databaseContext)
    {
        this.databaseContext = databaseContext;
    }

    // Lit les demandes dans cet état, la plus ancienne d'abord. Deux demandes du même instant gardent toujours le même ordre.
    // L'adresse où répondre est celle du compte, lue maintenant, ou celle du visiteur.
    public async Task<List<AdminSupportRequestItem>> FindRequestsAsync(SupportRequestStatus status, CancellationToken cancellationToken = default)
    {
        return await (
                from request in databaseContext.SupportRequests.AsNoTracking()
                join account in databaseContext.Users.AsNoTracking() on request.UserId equals (Guid?)account.Id into accounts
                from account in accounts.DefaultIfEmpty()
                where request.Status == status
                orderby request.SentAt, request.Id
                select new AdminSupportRequestItem(
                    request.Id,
                    request.SentAt,
                    request.Status,
                    account != null ? account.Email : request.ContactEmail!,
                    request.UserId,
                    databaseContext.SupportResponses.Count(response => response.SupportRequestId == request.Id)))
            .ToListAsync(cancellationToken);
    }

    // Lit la demande, l'adresse où répondre, puis ses réponses de la plus ancienne à la plus récente.
    public async Task<AdminSupportRequestDetails?> FindRequestDetailsAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var found = await (
                from request in databaseContext.SupportRequests.AsNoTracking()
                join account in databaseContext.Users.AsNoTracking() on request.UserId equals (Guid?)account.Id into accounts
                from account in accounts.DefaultIfEmpty()
                where request.Id == requestId
                select new { Request = request, ReplyAddress = account != null ? account.Email : request.ContactEmail! })
            .SingleOrDefaultAsync(cancellationToken);

        if (found == null)
        {
            return null;
        }

        var responses = await databaseContext.SupportResponses.AsNoTracking()
            .Where(response => response.SupportRequestId == requestId)
            .OrderBy(response => response.SentAt)
            .ThenBy(response => response.Id)
            .Select(response => new SupportResponseView(response.Content, response.SentAt))
            .ToListAsync(cancellationToken);

        return new AdminSupportRequestDetails(
            found.Request.Id,
            found.Request.SentAt,
            found.Request.Status,
            found.ReplyAddress,
            found.Request.UserId,
            found.Request.Message,
            responses);
    }

    // Lit la demande pour la changer : elle est suivie, et sa version est contrôlée à l'enregistrement.
    public async Task<SupportRequest?> FindRequestAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        return await databaseContext.SupportRequests.SingleOrDefaultAsync(request => request.Id == requestId, cancellationToken);
    }

    // La ligne de la demande sera écrite même si son état n'a pas changé : cette écriture contrôle en base sa version,
    // et elle échoue si une autre requête l'a écrite depuis sa lecture.
    public void PrepareRequestCheck(SupportRequest request)
    {
        databaseContext.Entry(request).Property(nameof(SupportRequest.Status)).IsModified = true;
    }

    // Garde de côté la réponse : elle ne sera écrite qu'avec les autres changements.
    public void PrepareResponse(SupportResponse response)
    {
        databaseContext.SupportResponses.Add(response);
    }

    // Compte les comptes dans le même ordre que l'état d'un compte (voir AccountStateRules) : une demande de suppression passe
    // avant tout, puis l'adresse non confirmée, puis le blocage en cours. Les quatre nombres viennent d'une seule requête, donc
    // du même instant de la base : un compte qui change d'état pendant la lecture est compté une fois, dans un seul état.
    public async Task<AdministrationStatistics> CountStatisticsAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var accounts = await databaseContext.Users.AsNoTracking()
            .GroupBy(user => 1)
            .Select(all => new
            {
                BeingDeleted = all.Count(user => user.DeletionRequestedAt != null),
                NotConfirmed = all.Count(user => user.DeletionRequestedAt == null && !user.IsEmailConfirmed),
                Locked = all.Count(user =>
                    user.DeletionRequestedAt == null && user.IsEmailConfirmed && user.LockoutEndsAt != null && user.LockoutEndsAt > now),
                Active = all.Count(user =>
                    user.DeletionRequestedAt == null && user.IsEmailConfirmed && (user.LockoutEndsAt == null || user.LockoutEndsAt <= now))
            })
            .SingleOrDefaultAsync(cancellationToken);
        var activeConnections = await databaseContext.BankConnections.AsNoTracking()
            .CountAsync(connection => connection.Status == BankConnectionStatus.Active, cancellationToken);
        var requestsByStatus = await databaseContext.SupportRequests.AsNoTracking()
            .GroupBy(request => request.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        int CountRequests(SupportRequestStatus status)
        {
            return requestsByStatus.SingleOrDefault(group => group.Status == status)?.Count ?? 0;
        }

        return new AdministrationStatistics(
            accounts?.NotConfirmed ?? 0,
            accounts?.Active ?? 0,
            accounts?.Locked ?? 0,
            accounts?.BeingDeleted ?? 0,
            activeConnections,
            CountRequests(SupportRequestStatus.Sent),
            CountRequests(SupportRequestStatus.InProgress),
            CountRequests(SupportRequestStatus.Resolved));
    }
}
