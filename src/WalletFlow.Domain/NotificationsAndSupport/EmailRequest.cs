using System.Net;

namespace WalletFlow.Domain.NotificationsAndSupport;

// Un e-mail qui attend dans la file. La file est gardée en base : un redémarrage du serveur ne perd aucune demande (SS §9).
// Correspond à la table demande_email du MPD (DD §9.4). Les dates sont en UTC.
// Une demande ne contient aucun secret : le lien d'un e-mail n'est créé qu'au moment de l'envoi.
// Chaque sorte de demande se crée par sa propre méthode, avec exactement ce dont elle a besoin. Ensuite, elle ne peut
// que noter ses essais ratés, jusqu'au dernier.
public class EmailRequest
{
    private short failedTryCount = 0;
    private DateTime nextTryAt;

    // id_demande_email
    public Guid Id { get; init; } = Guid.NewGuid();

    // type_demande_email
    public EmailRequestType Type { get; init; }

    // adresse_email_demande : seulement quand l'e-mail ne part pas à l'adresse du compte : l'adresse saisie pour une
    // récupération, la nouvelle adresse à confirmer, ou l'ancienne adresse prévenue.
    public string? EmailAddress { get; init; }

    // adresse_ip_demande : celle de la requête qui a demandé l'e-mail, pour le journal de sécurité.
    public IPAddress? IpAddress { get; init; }

    // date_demande_email : donnée par l'horloge de l'application. C'est aussi la date du premier essai.
    public required DateTime RequestedAt { get; init; }

    // nb_essais_envoi : les essais déjà ratés.
    public short FailedTryCount
    {
        get => failedTryCount;
        init => failedTryCount = value;
    }

    // date_prochain_essai
    public DateTime NextTryAt
    {
        get => nextTryAt;
        init => nextTryAt = value;
    }

    // id_utilisateur : vide pour une récupération, qui entre dans la file avant de savoir si l'adresse a un compte,
    // et pour une réponse au support, qui lit son adresse dans sa demande.
    public Guid? UserId { get; init; }

    // id_alerte : seulement pour un e-mail d'alerte, lié à l'alerte qu'il annonce.
    public Guid? AlertId { get; init; }

    // id_reponse : seulement pour une réponse au support, liée à la réponse qu'elle envoie.
    public Guid? SupportResponseId { get; init; }

    // Le visiteur demande à retrouver son mot de passe, avec cette adresse, déjà vérifiée et en minuscules (GR Auth).
    public static EmailRequest ForPasswordRecovery(string emailAddress, IPAddress? ipAddress, DateTime now)
    {
        return Create(EmailRequestType.PasswordRecovery, null, emailAddress, ipAddress, now);
    }

    // Un essai de connexion vient de bloquer le compte : son propriétaire reçoit un lien de déblocage (GR Auth).
    public static EmailRequest ForAccountUnlock(Guid userId, IPAddress? ipAddress, DateTime now)
    {
        return Create(EmailRequestType.AccountUnlock, userId, null, ipAddress, now);
    }

    // L'utilisateur demande cette nouvelle adresse, déjà vérifiée et en minuscules (GR Compte).
    public static EmailRequest ForEmailChange(Guid userId, string newEmailAddress, IPAddress? ipAddress, DateTime now)
    {
        return Create(EmailRequestType.EmailChange, userId, newEmailAddress, ipAddress, now);
    }

    // La nouvelle adresse vient d'être confirmée : l'adresse que le compte quitte est prévenue (GR Compte).
    public static EmailRequest ForEmailChangedNotice(Guid userId, string oldEmailAddress, IPAddress? ipAddress, DateTime now)
    {
        return Create(EmailRequestType.EmailChangedNotice, userId, oldEmailAddress, ipAddress, now);
    }

    // Le compte vient de se connecter depuis un appareil qu'il ne connaissait pas (GR Notif).
    public static EmailRequest ForNewDevice(Guid userId, IPAddress? ipAddress, DateTime now)
    {
        return Create(EmailRequestType.NewDevice, userId, null, ipAddress, now);
    }

    // Le mot de passe du compte vient de changer (GR Notif).
    public static EmailRequest ForPasswordChange(Guid userId, IPAddress? ipAddress, DateTime now)
    {
        return Create(EmailRequestType.PasswordChange, userId, null, ipAddress, now);
    }

    // La double authentification du compte vient d'être désactivée (GR Notif).
    public static EmailRequest ForTwoFactorDeactivation(Guid userId, IPAddress? ipAddress, DateTime now)
    {
        return Create(EmailRequestType.TwoFactorDeactivation, userId, null, ipAddress, now);
    }

    // Un code de secours du compte vient de servir (GR Notif).
    public static EmailRequest ForBackupCodeUse(Guid userId, IPAddress? ipAddress, DateTime now)
    {
        return Create(EmailRequestType.BackupCodeUse, userId, null, ipAddress, now);
    }

    // L'utilisateur vient de demander la suppression de son compte (GR Suppression).
    public static EmailRequest ForDeletionRequest(Guid userId, IPAddress? ipAddress, DateTime now)
    {
        return Create(EmailRequestType.DeletionRequest, userId, null, ipAddress, now);
    }

    // Une connexion vient d'annuler la suppression du compte (GR Suppression).
    public static EmailRequest ForDeletionCancellation(Guid userId, IPAddress? ipAddress, DateTime now)
    {
        return Create(EmailRequestType.DeletionCancellation, userId, null, ipAddress, now);
    }

    // Le compte vient d'être effacé, dans la même transaction : la demande ne vise plus de compte, et garde son adresse, en
    // minuscules comme celle du compte, jusqu'à l'envoi (DD §9.4). Aucune requête ne la demande : elle n'a pas d'adresse IP.
    public static EmailRequest ForAccountErasure(string erasedAccountEmail, DateTime now)
    {
        return Create(EmailRequestType.AccountErasure, null, erasedAccountEmail, null, now);
    }

    // L'administrateur fait envoyer au compte le lien qui désactive sa double authentification (GR Admin).
    public static EmailRequest ForTwoFactorDeactivationLink(Guid userId, IPAddress? ipAddress, DateTime now)
    {
        return Create(EmailRequestType.TwoFactorDeactivationLink, userId, null, ipAddress, now);
    }

    // L'administrateur vient d'écrire cette réponse. Elle ne vise aucun compte, et ne garde aucune adresse : elle part à
    // l'adresse lue dans sa demande au moment de l'envoi, celle du compte ou celle du visiteur (DD §9.4).
    public static EmailRequest ForSupportResponse(SupportResponse response, IPAddress? ipAddress)
    {
        return new EmailRequest
        {
            Type = EmailRequestType.SupportResponse,
            SupportResponseId = response.Id,
            IpAddress = ipAddress,
            RequestedAt = response.SentAt,
            NextTryAt = response.SentAt
        };
    }

    // Une alerte vient d'être créée pour l'utilisateur (DD §9.3). Aucune requête ne la demande : elle n'a pas d'adresse IP.
    public static EmailRequest ForAlert(Alert alert)
    {
        return new EmailRequest
        {
            Type = EmailRequestType.Alert,
            UserId = alert.UserId,
            AlertId = alert.Id,
            RequestedAt = alert.CreatedAt,
            NextTryAt = alert.CreatedAt
        };
    }

    // Note un essai raté. Renvoie vrai si un autre essai est prévu, à sa date ; faux si c'était le dernier :
    // la demande est alors abandonnée (SS §9).
    public bool RecordFailedTry(DateTime now)
    {
        if (failedTryCount >= EmailRequestRules.MaximumTries)
        {
            throw new InvalidOperationException("Cette demande a déjà fait tous ses essais.");
        }

        failedTryCount = (short)(failedTryCount + 1);

        if (failedTryCount == EmailRequestRules.MaximumTries)
        {
            return false;
        }

        nextTryAt = now + EmailRequestRules.FindWaitAfterFailures(failedTryCount);
        return true;
    }

    // Crée une demande dont le premier essai part tout de suite.
    private static EmailRequest Create(
        EmailRequestType type,
        Guid? userId,
        string? emailAddress,
        IPAddress? ipAddress,
        DateTime now)
    {
        return new EmailRequest
        {
            Type = type,
            UserId = userId,
            EmailAddress = emailAddress,
            IpAddress = ipAddress,
            RequestedAt = now,
            NextTryAt = now
        };
    }
}
