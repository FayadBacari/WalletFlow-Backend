using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Application.Emails;

// Lit ce que porte une demande de la file selon son type. La base refuse une demande à qui il manque ce que son type demande
// (DD §9.4) : un manque ici est une erreur, jamais un cas normal.
public static class EmailRequestFields
{
    // L'adresse que porte la demande. La base refuse une récupération ou un changement d'adresse sans elle.
    public static string ReadEmailAddress(EmailRequest request)
    {
        if (request.EmailAddress is not string emailAddress)
        {
            throw new InvalidOperationException("Cette demande d'e-mail n'a pas d'adresse.");
        }

        return emailAddress;
    }

    // Le compte que vise la demande. La base refuse toute demande sans compte, sauf une récupération et une réponse au support.
    public static Guid ReadUserId(EmailRequest request)
    {
        if (request.UserId is not Guid userId)
        {
            throw new InvalidOperationException("Cette demande d'e-mail ne vise aucun compte.");
        }

        return userId;
    }

    // La réponse qu'envoie la demande. La base refuse l'e-mail d'une réponse sans elle.
    public static Guid ReadSupportResponseId(EmailRequest request)
    {
        if (request.SupportResponseId is not Guid responseId)
        {
            throw new InvalidOperationException("Cette demande d'e-mail n'envoie aucune réponse du support.");
        }

        return responseId;
    }

    // L'alerte qu'annonce la demande. La base refuse un e-mail d'alerte sans elle.
    public static Guid ReadAlertId(EmailRequest request)
    {
        if (request.AlertId is not Guid alertId)
        {
            throw new InvalidOperationException("Cette demande d'e-mail n'annonce aucune alerte.");
        }

        return alertId;
    }
}
