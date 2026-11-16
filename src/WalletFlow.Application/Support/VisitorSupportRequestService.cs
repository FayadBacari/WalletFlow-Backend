using WalletFlow.Application.Persistence;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Support;

// Cas d'utilisation : un visiteur non connecté écrit au support, avec l'adresse où lui répondre (GR Admin), par exemple quand
// son compte est bloqué. Aucun e-mail de confirmation ne part : l'adresse saisie n'est pas vérifiée, et un tel e-mail
// permettrait d'en envoyer à n'importe qui. La limite de 3 demandes par heure et par adresse IP est celle de la route (SS §6).
public class VisitorSupportRequestService
{
    private readonly ISupportRequestRepository supportRepository;
    private readonly IDatabaseUnitOfWork unitOfWork;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux demandes, l'enregistrement, et l'horloge qui date la demande.
    public VisitorSupportRequestService(ISupportRequestRepository supportRepository, IDatabaseUnitOfWork unitOfWork, TimeProvider clock)
    {
        this.supportRepository = supportRepository;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
    }

    // Enregistre le message tapé et l'adresse saisie, sous leur forme enregistrée, s'ils respectent leur règle.
    // Les deux sont vérifiés avant de répondre : le visiteur apprend en une fois tout ce qui ne va pas.
    public async Task<VisitorSupportRequestResult> SendAsync(string? typedContactEmail, string? typedMessage)
    {
        var contactEmail = EmailAddressRules.Normalize(typedContactEmail ?? string.Empty);
        var message = SupportRules.NormalizeText(typedMessage);
        var isContactEmailInvalid = !EmailAddressRules.IsValid(contactEmail);
        var isMessageInvalid = !SupportRules.IsTextValid(message);

        if (isContactEmailInvalid || isMessageInvalid)
        {
            return new VisitorSupportRequestResult.RefusedByRules
            {
                IsContactEmailInvalid = isContactEmailInvalid,
                IsMessageInvalid = isMessageInvalid
            };
        }

        supportRepository.PrepareRequest(SupportRequest.FromVisitor(contactEmail, message, clock.GetUtcNow().UtcDateTime));

        if (!await unitOfWork.TrySaveChangesAsync())
        {
            return new VisitorSupportRequestResult.NotSaved();
        }

        return new VisitorSupportRequestResult.Sent();
    }
}
