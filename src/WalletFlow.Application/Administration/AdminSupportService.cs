using System.Net;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Application.Administration;

// Cas d'utilisation : l'administrateur lit les demandes de support, y répond, et les marque « résolues » (GR Admin). Seul lui
// répond. Chaque réponse part par e-mail avec son texte, à l'adresse du compte ou du visiteur, et passe la demande « en cours »,
// même une demande déjà résolue. Chaque réponse et chaque demande résolue sont notées au journal, avec le compte qui a écrit la
// demande ; celle d'un visiteur ne vise aucun compte (SS §11).
public class AdminSupportService
{
    private readonly IAdministrationRepository administrationRepository;
    private readonly IEmailRequestRepository emailRequests;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly TimeProvider clock;

    // Reçoit l'accès de l'administrateur, la file d'e-mails, l'enregistrement avec réessai, le journal et l'horloge.
    public AdminSupportService(
        IAdministrationRepository administrationRepository,
        IEmailRequestRepository emailRequests,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        TimeProvider clock)
    {
        this.administrationRepository = administrationRepository;
        this.emailRequests = emailRequests;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.clock = clock;
    }

    // Donne les demandes dans cet état, la plus ancienne d'abord : l'administrateur y répond dans l'ordre.
    public async Task<List<AdminSupportRequestItem>> ListAsync(SupportRequestStatus status)
    {
        return await administrationRepository.FindRequestsAsync(status);
    }

    // Donne une demande avec ses réponses, ou null si elle n'existe pas.
    public async Task<AdminSupportRequestDetails?> ReadAsync(Guid requestId)
    {
        return await administrationRepository.FindRequestDetailsAsync(requestId);
    }

    // Répond à la demande avec ce texte, sous sa forme enregistrée, s'il respecte la règle du texte.
    public async Task<AdminSupportAnswerResult> AnswerAsync(Guid administratorId, Guid requestId, string? typedContent, IPAddress? ipAddress)
    {
        var content = SupportRules.NormalizeText(typedContent);

        if (!SupportRules.IsTextValid(content))
        {
            return new AdminSupportAnswerResult.ContentBreaksRules();
        }

        var saved = await savingService.SaveWithRetriesAsync<bool?>(
            () => PrepareAnswerAsync(administratorId, requestId, content, ipAddress),
            null);

        if (saved != true)
        {
            return saved == false ? new AdminSupportAnswerResult.NotFound() : new AdminSupportAnswerResult.NotSaved();
        }

        return await ReadAfterChangeAsync<AdminSupportAnswerResult>(
            requestId,
            details => new AdminSupportAnswerResult.Answered { Request = details },
            new AdminSupportAnswerResult.NotFound());
    }

    // Marque la demande « résolue », avec ou sans réponse.
    public async Task<AdminSupportResolutionResult> ResolveAsync(Guid administratorId, Guid requestId, IPAddress? ipAddress)
    {
        var outcome = await savingService.SaveWithRetriesAsync<AdminSupportResolutionResult?>(
            () => PrepareResolutionAsync(administratorId, requestId, ipAddress),
            new AdminSupportResolutionResult.NotSaved());

        if (outcome != null)
        {
            return outcome;
        }

        return await ReadAfterChangeAsync<AdminSupportResolutionResult>(
            requestId,
            details => new AdminSupportResolutionResult.Resolved { Request = details },
            new AdminSupportResolutionResult.NotFound());
    }

    // Un passage : lit la demande, puis prépare la réponse, son e-mail, la demande « en cours » et l'action de l'administrateur.
    // La ligne de la demande est réécrite même si elle était déjà « en cours » : une réponse et une demande résolue au même
    // instant passent ainsi l'une après l'autre. Donne vrai si tout est préparé, faux si la demande n'existe pas.
    private async Task<PreparedChanges<bool?>> PrepareAnswerAsync(Guid administratorId, Guid requestId, string content, IPAddress? ipAddress)
    {
        var request = await administrationRepository.FindRequestAsync(requestId);

        if (request == null)
        {
            return PreparedChanges<bool?>.NothingToSave(false);
        }

        var response = SupportResponse.Create(request.Id, content, clock.GetUtcNow().UtcDateTime);
        request.NoteResponse();
        administrationRepository.PrepareRequestCheck(request);
        administrationRepository.PrepareResponse(response);
        emailRequests.PrepareRequest(EmailRequest.ForSupportResponse(response, ipAddress));
        securityJournal.PrepareAdminAction(administratorId, request.UserId, ipAddress);

        return PreparedChanges<bool?>.SaveThenReturn(true);
    }

    // Un passage : lit la demande, puis prépare son nouvel état et l'action de l'administrateur. Donne null si tout est préparé,
    // ou la raison du refus.
    private async Task<PreparedChanges<AdminSupportResolutionResult?>> PrepareResolutionAsync(Guid administratorId, Guid requestId, IPAddress? ipAddress)
    {
        var request = await administrationRepository.FindRequestAsync(requestId);

        if (request == null)
        {
            return PreparedChanges<AdminSupportResolutionResult?>.NothingToSave(new AdminSupportResolutionResult.NotFound());
        }

        if (request.Status == SupportRequestStatus.Resolved)
        {
            return PreparedChanges<AdminSupportResolutionResult?>.NothingToSave(new AdminSupportResolutionResult.AlreadyResolved());
        }

        request.MarkAsResolved();
        securityJournal.PrepareAdminAction(administratorId, request.UserId, ipAddress);

        return PreparedChanges<AdminSupportResolutionResult?>.SaveThenReturn(null);
    }

    // Relit la demande après son changement, pour la montrer telle qu'elle est enregistrée. Une demande effacée juste après
    // n'existe plus.
    private async Task<TResult> ReadAfterChangeAsync<TResult>(Guid requestId, Func<AdminSupportRequestDetails, TResult> found, TResult notFound)
    {
        var details = await administrationRepository.FindRequestDetailsAsync(requestId);

        return details == null ? notFound : found(details);
    }
}
