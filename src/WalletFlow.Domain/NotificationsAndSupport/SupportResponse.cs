namespace WalletFlow.Domain.NotificationsAndSupport;

// Réponse de l'administrateur à une demande de support.
// Correspond à la table reponse_support du MPD (DD §10.2). Les dates sont en UTC.
public class SupportResponse
{
    // id_reponse
    public Guid Id { get; init; } = Guid.NewGuid();

    // contenu_reponse
    public string Content { get; init; } = string.Empty;

    // date_reponse : donnée par l'horloge de l'application.
    public required DateTime SentAt { get; init; }

    // id_demande
    public Guid SupportRequestId { get; init; }

    // L'administrateur répond à cette demande avec ce texte, déjà sous sa forme enregistrée (GR Admin). Le texte suit la même
    // règle qu'un message : il part par e-mail, et l'utilisateur le lit dans sa demande (SS §5).
    public static SupportResponse Create(Guid supportRequestId, string content, DateTime now)
    {
        if (content != SupportRules.NormalizeText(content) || !SupportRules.IsTextValid(content))
        {
            throw new ArgumentException("La réponse ne respecte pas la règle du texte du support.", nameof(content));
        }

        return new SupportResponse
        {
            Content = content,
            SentAt = now,
            SupportRequestId = supportRequestId
        };
    }
}
