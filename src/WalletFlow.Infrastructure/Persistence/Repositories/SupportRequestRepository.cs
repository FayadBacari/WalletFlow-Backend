using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Support;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Lit et écrit les demandes de support et leurs réponses dans la base PostgreSQL. Chaque lecture d'un utilisateur filtre sur
// l'utilisateur de la session (SS §4). Les demandes et les réponses lues pour être montrées ne sont pas suivies par EF Core.
public class SupportRequestRepository : ISupportRequestRepository
{
    private readonly WalletFlowDatabaseContext databaseContext;

    // Reçoit l'accès à la base de la requête en cours.
    public SupportRequestRepository(WalletFlowDatabaseContext databaseContext)
    {
        this.databaseContext = databaseContext;
    }

    // Lit la ligne de l'utilisateur. Elle est suivie : son contrôle à l'enregistrement en vérifie la version.
    public async Task<User?> FindUserAsync(Guid userId)
    {
        return await databaseContext.Users.SingleOrDefaultAsync(user => user.Id == userId);
    }

    // La ligne de l'utilisateur sera écrite même si rien n'y a changé : cette écriture contrôle en base sa version,
    // et elle échoue si une autre requête l'a écrite depuis sa lecture. EF Core n'écrit une ligne que si une colonne
    // a changé : le compteur d'essais est donc réécrit avec sa propre valeur, comme dans UserAccountRepository.
    public void PrepareUserCheck(User user)
    {
        databaseContext.Entry(user).Property(nameof(User.FailedLoginAttemptCount)).IsModified = true;
    }

    // Compte les demandes de l'utilisateur envoyées depuis ce moment, ce moment compris.
    public async Task<int> CountSentSinceAsync(Guid userId, DateTime since)
    {
        return await databaseContext.SupportRequests.CountAsync(request =>
            request.UserId == userId
            && request.SentAt >= since);
    }

    // Garde de côté la nouvelle demande : elle ne sera écrite qu'avec les autres changements.
    public void PrepareRequest(SupportRequest request)
    {
        databaseContext.SupportRequests.Add(request);
    }

    // Lit les demandes de l'utilisateur, la plus récente d'abord. Deux demandes du même instant gardent toujours le même ordre.
    public async Task<List<SupportRequestSummary>> FindUserRequestsAsync(Guid userId)
    {
        return await databaseContext.SupportRequests.AsNoTracking()
            .Where(request => request.UserId == userId)
            .OrderByDescending(request => request.SentAt)
            .ThenBy(request => request.Id)
            .Select(request => new SupportRequestSummary(
                request.Id,
                request.SentAt,
                request.Status,
                databaseContext.SupportResponses.Count(response => response.SupportRequestId == request.Id)))
            .ToListAsync();
    }

    // Lit la demande par son identifiant et par son utilisateur : celle d'un autre, ou d'un visiteur, n'est jamais trouvée.
    // Ses réponses viennent de la plus ancienne à la plus récente.
    public async Task<SupportRequestDetails?> FindUserRequestAsync(Guid userId, Guid requestId)
    {
        var request = await databaseContext.SupportRequests.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == requestId && candidate.UserId == userId);

        if (request == null)
        {
            return null;
        }

        var responses = await databaseContext.SupportResponses.AsNoTracking()
            .Where(response => response.SupportRequestId == request.Id)
            .OrderBy(response => response.SentAt)
            .ThenBy(response => response.Id)
            .Select(response => new SupportResponseView(response.Content, response.SentAt))
            .ToListAsync();

        return new SupportRequestDetails(request.Id, request.SentAt, request.Status, request.Message, responses);
    }

    // Lit la réponse, sa demande, et l'adresse du compte de la demande s'il y en a un : un changement d'adresse fait entre la
    // réponse et l'envoi est suivi. Une demande de compte répond au compte, même si elle avait une adresse de contact.
    public async Task<SupportResponseEmailContent?> ReadResponseEmailAsync(Guid responseId)
    {
        return await (
                from response in databaseContext.SupportResponses.AsNoTracking()
                join request in databaseContext.SupportRequests.AsNoTracking() on response.SupportRequestId equals request.Id
                join account in databaseContext.Users.AsNoTracking() on request.UserId equals (Guid?)account.Id into accounts
                from account in accounts.DefaultIfEmpty()
                where response.Id == responseId
                select new SupportResponseEmailContent(
                    account != null ? account.Email : request.ContactEmail!,
                    request.SentAt,
                    response.Content,
                    request.UserId))
            .SingleOrDefaultAsync();
    }

    // Efface tout de suite les demandes envoyées avant ce moment. La base efface avec elles leurs réponses, puis les e-mails
    // de ces réponses encore en attente (ON DELETE CASCADE).
    public async Task<int> DeleteSentBeforeAsync(DateTime limit)
    {
        return await databaseContext.SupportRequests
            .Where(request => request.SentAt < limit)
            .ExecuteDeleteAsync();
    }
}
