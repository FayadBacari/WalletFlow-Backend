using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Domain.NotificationsAndSupport;

// Message envoyé au support par un utilisateur ou par un visiteur.
// Correspond à la table demande_support du MPD (DD §10.1). Les dates sont en UTC.
// Une demande se crée par la méthode de celui qui l'écrit, avec exactement ce dont elle a besoin.
public class SupportRequest
{
    private SupportRequestStatus status = SupportRequestStatus.Sent;

    // id_demande
    public Guid Id { get; init; } = Guid.NewGuid();

    // message_demande : contenu non fiable, toujours affiché comme du texte.
    public string Message { get; init; } = string.Empty;

    // email_contact : obligatoire quand la demande vient d'un visiteur.
    public string? ContactEmail { get; init; }

    // statut_demande : envoyée par défaut, « en cours » dès une réponse, « résolue » quand l'administrateur la marque ainsi.
    public SupportRequestStatus Status
    {
        get => status;
        init => status = value;
    }

    // date_demande : donnée par l'horloge de l'application. La demande est effacée un an après, avec ses réponses (GR Admin).
    public required DateTime SentAt { get; init; }

    // id_utilisateur : vide quand la demande vient d'un visiteur.
    public Guid? UserId { get; init; }

    // L'utilisateur connecté écrit ce message, déjà sous sa forme enregistrée (GR Admin).
    public static SupportRequest FromUser(Guid userId, string message, DateTime now)
    {
        ThrowIfMessageBreaksRules(message);

        return new SupportRequest
        {
            Message = message,
            SentAt = now,
            UserId = userId
        };
    }

    // Un visiteur écrit ce message, avec l'adresse où lui répondre, déjà vérifiée et en minuscules (GR Admin).
    public static SupportRequest FromVisitor(string contactEmail, string message, DateTime now)
    {
        ThrowIfMessageBreaksRules(message);

        if (!EmailAddressRules.IsValid(contactEmail) || contactEmail != EmailAddressRules.Normalize(contactEmail))
        {
            throw new ArgumentException("L'adresse de contact doit être valide et en minuscules.", nameof(contactEmail));
        }

        return new SupportRequest
        {
            Message = message,
            ContactEmail = contactEmail,
            SentAt = now
        };
    }

    // Une réponse vient de partir : la demande est « en cours ». Une demande résolue l'est donc de nouveau : la réponse la
    // rouvre, jusqu'à ce que l'administrateur la marque encore « résolue » (GR Admin).
    public void NoteResponse()
    {
        status = SupportRequestStatus.InProgress;
    }

    // L'administrateur marque la demande « résolue », avec ou sans réponse : un spam ou un doublon n'en demande pas (GR Admin).
    public void MarkAsResolved()
    {
        if (status == SupportRequestStatus.Resolved)
        {
            throw new InvalidOperationException("Cette demande est déjà résolue.");
        }

        status = SupportRequestStatus.Resolved;
    }

    // Refuse un message qui n'est pas sous sa forme enregistrée, ou qui ne suit pas la règle du texte (SS §5).
    private static void ThrowIfMessageBreaksRules(string message)
    {
        if (message != SupportRules.NormalizeText(message) || !SupportRules.IsTextValid(message))
        {
            throw new ArgumentException("Le message ne respecte pas la règle du texte du support.", nameof(message));
        }
    }
}
