using WalletFlow.Application.Persistence;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Application.Support;

// Cas d'utilisation : l'utilisateur connecté écrit au support (GR Admin), dans la limite de 10 demandes sur les dernières
// 24 heures (SS §6). Aucun e-mail ne part : la demande se suit dans la liste des demandes, et chaque réponse arrivera par e-mail.
public class SupportRequestSendingService
{
    private readonly ISupportRequestRepository supportRepository;
    private readonly DatabaseChangesSavingService savingService;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux demandes, l'enregistrement avec réessai, et l'horloge qui date la demande.
    public SupportRequestSendingService(
        ISupportRequestRepository supportRepository,
        DatabaseChangesSavingService savingService,
        TimeProvider clock)
    {
        this.supportRepository = supportRepository;
        this.savingService = savingService;
        this.clock = clock;
    }

    // Enregistre le message tapé, sous sa forme enregistrée, s'il respecte la règle du texte et la limite des demandes.
    public async Task<SupportRequestSendingResult> SendAsync(Guid userId, string? typedMessage)
    {
        var message = SupportRules.NormalizeText(typedMessage);

        if (!SupportRules.IsTextValid(message))
        {
            return new SupportRequestSendingResult.MessageBreaksRules();
        }

        return await savingService.SaveWithRetriesAsync(
            () => PrepareRequestAsync(userId, message),
            new SupportRequestSendingResult.NotSaved());
    }

    // Un passage : lit d'abord la ligne de l'utilisateur, compte ses demandes des dernières 24 heures, puis prépare la nouvelle.
    // La ligne de l'utilisateur est réécrite en même temps : de deux demandes simultanées, la seconde échoue et recompte.
    // Deux demandes ne peuvent donc pas passer ensemble le contrôle du nombre maximum.
    private async Task<PreparedChanges<SupportRequestSendingResult>> PrepareRequestAsync(Guid userId, string message)
    {
        var user = await supportRepository.FindUserAsync(userId);

        if (user == null)
        {
            return PreparedChanges<SupportRequestSendingResult>.NothingToSave(new SupportRequestSendingResult.NotSaved());
        }

        var now = clock.GetUtcNow().UtcDateTime;

        if (await supportRepository.CountSentSinceAsync(userId, now - SupportRules.RequestDay) >= SupportRules.MaximumRequestsPerDay)
        {
            return PreparedChanges<SupportRequestSendingResult>.NothingToSave(new SupportRequestSendingResult.TooManyRequests());
        }

        var request = SupportRequest.FromUser(userId, message, now);
        supportRepository.PrepareRequest(request);
        supportRepository.PrepareUserCheck(user);

        var details = new SupportRequestDetails(request.Id, request.SentAt, request.Status, request.Message, new List<SupportResponseView>());

        return PreparedChanges<SupportRequestSendingResult>.SaveThenReturn(new SupportRequestSendingResult.Sent { Request = details });
    }
}
