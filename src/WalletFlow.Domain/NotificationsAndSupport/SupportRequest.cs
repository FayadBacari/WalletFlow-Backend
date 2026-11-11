namespace WalletFlow.Domain.NotificationsAndSupport;

// Message envoyé au support par un utilisateur ou par un visiteur.
// Correspond à la table demande_support du MPD (DD §10.1).
public class SupportRequest
{
    // id_demande
    public Guid Id { get; init; } = Guid.NewGuid();

    // message_demande : contenu non fiable, toujours affiché comme du texte.
    public string Message { get; init; } = string.Empty;

    // email_contact : obligatoire quand la demande vient d'un visiteur.
    public string? ContactEmail { get; init; }

    // statut_demande : envoyée par défaut.
    public SupportRequestStatus Status { get; init; } = SupportRequestStatus.Sent;

    // id_utilisateur : vide quand la demande vient d'un visiteur.
    public Guid? UserId { get; init; }
}
