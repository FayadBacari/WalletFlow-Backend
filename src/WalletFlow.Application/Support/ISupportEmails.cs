using WalletFlow.Application.Emails;

namespace WalletFlow.Application.Support;

// E-mails du support. L'Application choisit l'e-mail et ses données ; l'Infrastructure en écrit le texte (SS §9).
public interface ISupportEmails
{
    // Crée l'e-mail d'une réponse du support, avec son texte, pour la demande envoyée à cette date de Paris. Il finit par
    // l'adresse de la page de support, où écrire une nouvelle demande.
    EmailMessage CreateResponseEmail(string recipient, DateTime requestSentAtInParis, string responseText, string supportPageAddress);
}

// Ce que l'e-mail d'une réponse dit, lu au moment de l'envoi : l'adresse où répondre (celle du compte, ou celle du visiteur),
// la date de la demande, et le texte de la réponse. Le compte est vide pour un visiteur.
public record SupportResponseEmailContent(string RecipientAddress, DateTime RequestSentAt, string ResponseText, Guid? AccountId);
