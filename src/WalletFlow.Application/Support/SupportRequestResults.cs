namespace WalletFlow.Application.Support;

// Résultat d'une demande envoyée par l'utilisateur connecté : toujours une seule des formes écrites ici.
public abstract class SupportRequestSendingResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private SupportRequestSendingResult()
    {
    }

    // La demande est enregistrée, sans réponse pour l'instant.
    public sealed class Sent : SupportRequestSendingResult
    {
        public required SupportRequestDetails Request { get; init; }
    }

    // Le message ne respecte pas la règle du texte (SS §5).
    public sealed class MessageBreaksRules : SupportRequestSendingResult
    {
    }

    // L'utilisateur a déjà envoyé 10 demandes sur les dernières 24 heures (SS §6).
    public sealed class TooManyRequests : SupportRequestSendingResult
    {
    }

    // D'autres requêtes changent le compte sans arrêt, ou le compte n'existe plus : rien n'est enregistré.
    public sealed class NotSaved : SupportRequestSendingResult
    {
    }
}

// Résultat d'une demande envoyée par un visiteur : toujours une seule des formes écrites ici.
public abstract class VisitorSupportRequestResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private VisitorSupportRequestResult()
    {
    }

    // La demande est enregistrée. Le visiteur ne peut pas la suivre : la réponse lui arrivera par e-mail.
    public sealed class Sent : VisitorSupportRequestResult
    {
    }

    // L'adresse de contact, le message, ou les deux, ne respectent pas leur règle (SS §5).
    public sealed class RefusedByRules : VisitorSupportRequestResult
    {
        public required bool IsContactEmailInvalid { get; init; }
        public required bool IsMessageInvalid { get; init; }
    }

    // La base a refusé l'enregistrement : rien n'est enregistré.
    public sealed class NotSaved : VisitorSupportRequestResult
    {
    }
}
