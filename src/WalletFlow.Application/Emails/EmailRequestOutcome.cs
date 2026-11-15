using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Application.Emails;

// Ce qu'est devenue une demande de la file après un essai, pour le journal technique : toujours une seule des formes
// écrites dans ce fichier. Elles désignent l'utilisateur par son identifiant, jamais par son adresse (SS §7).
public abstract class EmailRequestOutcome
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private EmailRequestOutcome()
    {
    }

    // La demande n'est plus dans la file : un autre passage l'a déjà traitée, ou son compte a été effacé.
    public sealed class NoLongerQueued : EmailRequestOutcome
    {
    }

    // L'e-mail est parti, ou il n'y avait plus rien à envoyer : la demande est sortie de la file.
    public sealed class Finished : EmailRequestOutcome
    {
    }

    // L'envoi a raté : un autre essai est prévu.
    public sealed class WillRetry : EmailRequestOutcome
    {
        public required EmailRequestType Type { get; init; }

        // Le compte à qui l'e-mail était destiné, s'il y en a un.
        public required Guid? UserId { get; init; }

        public required short FailedTryCount { get; init; }
        public required DateTime NextTryAt { get; init; }
    }

    // Le dernier essai a raté : la demande est sortie de la file sans que l'e-mail parte.
    public sealed class GivenUp : EmailRequestOutcome
    {
        public required EmailRequestType Type { get; init; }

        // Le compte à qui l'e-mail était destiné, s'il y en a un.
        public required Guid? UserId { get; init; }
    }
}
