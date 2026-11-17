using WalletFlow.Application.Emails;
using WalletFlow.Application.Notifications;

namespace WalletFlow.Infrastructure.Emails;

// Texte en français des alertes de sécurité (GR Notif). Le moment est écrit à l'heure de Paris, en toutes lettres.
// Chaque alerte finit par l'adresse de la page de support, seule sur sa ligne (SS §9).
public class FrenchSecurityAlertEmails : ISecurityAlertEmails
{
    // Crée l'alerte d'une connexion depuis un appareil que le compte ne connaissait pas.
    public EmailMessage CreateNewDeviceEmail(string recipient, DateTime loginTimeInParis, string supportPageAddress)
    {
        var body = $"""
            Bonjour,

            Quelqu'un vient de se connecter à ton compte WalletFlow depuis un appareil que ton compte ne connaissait pas, {FrenchDates.WriteMoment(loginTimeInParis)} (heure de Paris).

            Si c'est toi, tu n'as rien à faire. Sinon, change tout de suite ton mot de passe : toutes les autres sessions seront fermées. Active aussi la double authentification, puis écris au support de WalletFlow :
            {supportPageAddress}
            """;

        return new EmailMessage(recipient, "Nouvelle connexion à ton compte WalletFlow", body);
    }

    // Crée l'alerte d'un changement de mot de passe.
    public EmailMessage CreatePasswordChangedEmail(string recipient, DateTime changeTimeInParis, string supportPageAddress)
    {
        var body = $"""
            Bonjour,

            Le mot de passe de ton compte WalletFlow vient d'être changé, {FrenchDates.WriteMoment(changeTimeInParis)} (heure de Paris).

            Si c'est toi, tu n'as rien à faire. Sinon, choisis tout de suite un nouveau mot de passe avec « Mot de passe oublié », puis écris au support de WalletFlow :
            {supportPageAddress}
            """;

        return new EmailMessage(recipient, "Le mot de passe de ton compte WalletFlow a changé", body);
    }

    // Crée l'alerte de la désactivation de la double authentification. Celui qui l'a désactivée connaissait le mot de passe :
    // l'alerte invite donc à en choisir un nouveau avant de la réactiver.
    public EmailMessage CreateTwoFactorDeactivatedEmail(string recipient, DateTime deactivationTimeInParis, string supportPageAddress)
    {
        var body = $"""
            Bonjour,

            La double authentification de ton compte WalletFlow vient d'être désactivée, {FrenchDates.WriteMoment(deactivationTimeInParis)} (heure de Paris).

            Si c'est toi, tu n'as rien à faire. Sinon, choisis tout de suite un nouveau mot de passe avec « Mot de passe oublié », réactive la double authentification, puis écris au support de WalletFlow :
            {supportPageAddress}
            """;

        return new EmailMessage(recipient, "La double authentification de ton compte WalletFlow est désactivée", body);
    }

    // Crée l'alerte de l'utilisation d'un code de secours. Celui qui s'en est servi connaissait aussi le mot de passe :
    // l'alerte invite à en choisir un nouveau, et à remplacer les codes de secours.
    public EmailMessage CreateBackupCodeUsedEmail(string recipient, DateTime useTimeInParis, string supportPageAddress)
    {
        var body = $"""
            Bonjour,

            Un code de secours de ton compte WalletFlow vient de servir, {FrenchDates.WriteMoment(useTimeInParis)} (heure de Paris). Ce code ne servira plus.

            Si c'est toi, tu n'as rien à faire. Sinon, quelqu'un connaît ton mot de passe et un de tes codes de secours : choisis tout de suite un nouveau mot de passe avec « Mot de passe oublié », puis désactive et réactive la double authentification pour recevoir de nouveaux codes de secours. Écris aussi au support de WalletFlow :
            {supportPageAddress}
            """;

        return new EmailMessage(recipient, "Un code de secours de ton compte WalletFlow a servi", body);
    }

    // Crée l'alerte de l'annulation d'une suppression. Celui qui s'est connecté connaissait le mot de passe : l'alerte invite
    // à en choisir un nouveau si ce n'est pas l'utilisateur.
    public EmailMessage CreateDeletionCancelledEmail(string recipient, DateTime cancellationTimeInParis, string supportPageAddress)
    {
        var body = $"""
            Bonjour,

            Une connexion à ton compte WalletFlow vient d'annuler la demande de suppression, {FrenchDates.WriteMoment(cancellationTimeInParis)} (heure de Paris). Ton compte et tes données sont gardés. Si ta banque était connectée, renouvelle son autorisation dans WalletFlow.

            Si c'est toi, tu n'as rien d'autre à faire. Sinon, quelqu'un connaît ton mot de passe : choisis-en tout de suite un nouveau avec « Mot de passe oublié », puis écris au support de WalletFlow :
            {supportPageAddress}
            """;

        return new EmailMessage(recipient, "La suppression de ton compte WalletFlow est annulée", body);
    }
}
