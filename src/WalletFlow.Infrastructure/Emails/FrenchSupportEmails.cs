using WalletFlow.Application.Emails;
using WalletFlow.Application.Support;

namespace WalletFlow.Infrastructure.Emails;

// Texte en français des e-mails du support. La réponse part en texte brut, telle que l'administrateur l'a écrite : un message
// piégé qu'elle citerait ne devient pas du HTML (SS §9).
public class FrenchSupportEmails : ISupportEmails
{
    // Crée l'e-mail d'une réponse : la date de la demande, le texte de la réponse, puis l'adresse de la page de support,
    // seule sur sa ligne, pour ajouter quelque chose avec une nouvelle demande (GR Admin).
    public EmailMessage CreateResponseEmail(string recipient, DateTime requestSentAtInParis, string responseText, string supportPageAddress)
    {
        var body = $"""
            Bonjour,

            Le support de WalletFlow a répondu à ta demande du {FrenchDates.WriteDay(DateOnly.FromDateTime(requestSentAtInParis))} :

            {responseText}

            Pour ajouter quelque chose, écris une nouvelle demande au support de WalletFlow :
            {supportPageAddress}
            """;

        return new EmailMessage(recipient, "Réponse du support de WalletFlow", body);
    }
}
