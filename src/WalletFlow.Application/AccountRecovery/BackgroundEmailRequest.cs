using System.Net;

namespace WalletFlow.Application.AccountRecovery;

// Une demande d'e-mail à traiter en arrière-plan : toujours une seule des formes écrites dans ce fichier.
// Chaque forme porte seulement ce dont elle a besoin : aucun champ n'est facultatif selon le type.
// Des classes plutôt que des records : un record afficherait l'adresse e-mail si on l'écrivait dans un journal.
public abstract class BackgroundEmailRequest
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private BackgroundEmailRequest()
    {
    }

    // Le type de la demande, pour le journal technique.
    public abstract BackgroundEmailRequestType Type { get; }

    public IPAddress? IpAddress { get; init; }

    // Un visiteur demande à retrouver son mot de passe.
    public sealed class PasswordRecovery : BackgroundEmailRequest
    {
        public override BackgroundEmailRequestType Type => BackgroundEmailRequestType.PasswordRecovery;

        // Adresse donnée par le visiteur.
        public required string EmailAddress { get; init; }
    }

    // Un essai de connexion vient de bloquer un compte : son propriétaire reçoit un lien de déblocage.
    public sealed class AccountUnlock : BackgroundEmailRequest
    {
        public override BackgroundEmailRequestType Type => BackgroundEmailRequestType.AccountUnlock;

        public required Guid AccountId { get; init; }
    }

    // L'utilisateur demande une nouvelle adresse : elle reçoit un lien si elle est libre, sinon son propriétaire est prévenu (GR Compte).
    public sealed class EmailChange : BackgroundEmailRequest
    {
        public override BackgroundEmailRequestType Type => BackgroundEmailRequestType.EmailChange;

        public required Guid AccountId { get; init; }

        // Nouvelle adresse demandée, déjà vérifiée et en minuscules.
        public required string NewEmailAddress { get; init; }
    }

    // La nouvelle adresse vient d'être confirmée : l'ancienne adresse est prévenue (GR Compte).
    public sealed class EmailChangedNotice : BackgroundEmailRequest
    {
        public override BackgroundEmailRequestType Type => BackgroundEmailRequestType.EmailChangedNotice;

        public required Guid AccountId { get; init; }

        // L'adresse que le compte vient de quitter : elle ne sert plus à se connecter.
        public required string OldEmailAddress { get; init; }
    }
}
