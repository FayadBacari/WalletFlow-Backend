namespace WalletFlow.Domain.NotificationsAndSupport;

// L'e-mail qu'une demande de la file doit envoyer (DD §9.4).
public enum EmailRequestType
{
    // Le lien pour choisir un nouveau mot de passe, ou l'invitation à s'inscrire à nouveau (GR Auth).
    PasswordRecovery,

    // Le lien de déblocage, envoyé quand un essai de connexion bloque le compte : c'est l'alerte « compte bloqué » (GR Notif).
    AccountUnlock,

    // Le lien qui confirme une nouvelle adresse, ou l'avis à son propriétaire si elle est déjà prise (GR Compte).
    EmailChange,

    // L'avis envoyé à l'ancienne adresse, une fois la nouvelle confirmée (GR Compte).
    EmailChangedNotice,

    // L'alerte d'une connexion depuis un appareil que le compte ne connaissait pas (GR Notif).
    NewDevice,

    // L'alerte d'un changement de mot de passe (GR Notif).
    PasswordChange,

    // L'alerte de la désactivation de la double authentification (GR Notif).
    TwoFactorDeactivation,

    // L'alerte de l'utilisation d'un code de secours (GR Notif).
    BackupCodeUse,

    // Le lien que l'administrateur fait envoyer pour désactiver la double authentification (GR Admin, GR Auth).
    TwoFactorDeactivationLink,

    // Une réponse du support, avec son texte, liée à la réponse qu'elle envoie (GR Admin).
    SupportResponse,

    // L'e-mail d'une des quatre alertes qu'on peut désactiver, lié à l'alerte qu'il annonce (DD §9.3).
    Alert
}
