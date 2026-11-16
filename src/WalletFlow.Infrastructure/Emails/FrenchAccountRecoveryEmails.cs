using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Emails;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Emails;

// Texte en français des e-mails de récupération et de déblocage. La durée de chaque lien vient de sa règle :
// le texte la suit si elle change.
public class FrenchAccountRecoveryEmails : IAccountRecoveryEmails
{
    // Crée l'e-mail qui contient le lien de récupération du mot de passe (GR Auth).
    public EmailMessage CreateRecoveryEmail(string recipient, string recoveryLink)
    {
        var body = $"""
            Bonjour,

            Pour choisir un nouveau mot de passe WalletFlow, ouvre ce lien :
            {recoveryLink}

            Ce lien est valable {FrenchDurations.Write(EmailTokenValidityDurations.PasswordRecoveryLink)} et ne sert qu'une seule fois.

            Si tu n'es pas à l'origine de cette demande, ignore ce message : ton mot de passe ne change pas.
            """;

        return new EmailMessage(recipient, "Choisis un nouveau mot de passe WalletFlow", body);
    }

    // Crée l'e-mail qui invite à s'inscrire à nouveau : une adresse non confirmée n'a pas de lien de récupération (GR Auth).
    public EmailMessage CreateSignupInvitationEmail(string recipient)
    {
        var body = """
            Bonjour,

            Tu as demandé à retrouver ton mot de passe WalletFlow, mais cette adresse n'est pas encore confirmée.

            Pour continuer, inscris-toi à nouveau avec cette adresse : tu choisiras un nouveau mot de passe et tu recevras un nouveau lien de confirmation.

            Si tu n'es pas à l'origine de cette demande, ignore ce message : rien n'a changé.
            """;

        return new EmailMessage(recipient, "Ton adresse e-mail WalletFlow n'est pas encore confirmée", body);
    }

    // Crée l'e-mail qui prévient du blocage et de sa durée, avec le lien de déblocage (GR Auth, SS §2).
    public EmailMessage CreateUnlockEmail(string recipient, string unlockLink, TimeSpan lockoutDuration)
    {
        var body = $"""
            Bonjour,

            Plusieurs essais de connexion ont échoué : ton compte WalletFlow est bloqué pendant {FrenchDurations.Write(lockoutDuration)}.

            Pour le débloquer sans attendre, ouvre ce lien :
            {unlockLink}

            Ce lien est valable {FrenchDurations.Write(EmailTokenValidityDurations.AccountUnlockLink)} et ne sert qu'une seule fois. Il ne te connecte pas : tu devras ensuite saisir ton mot de passe.

            Si ce n'est pas toi qui as essayé de te connecter, ton mot de passe n'a pas changé. Par précaution, tu peux en choisir un nouveau avec « Mot de passe oublié ».
            """;

        return new EmailMessage(recipient, "Ton compte WalletFlow est bloqué", body);
    }

    // Crée l'e-mail du lien qui désactive la double authentification, que le support fait envoyer à l'adresse du compte.
    // Il dit ce que le lien demande et quand il ne marche pas : le mot de passe, 7 jours sans changement, 5 essais (SS §2).
    public EmailMessage CreateTwoFactorDeactivationLinkEmail(string recipient, string deactivationLink, string supportPageAddress)
    {
        var body = $"""
            Bonjour,

            À ta demande, le support de WalletFlow t'envoie ce lien pour désactiver la double authentification de ton compte. Ouvre-le, puis saisis ton mot de passe :
            {deactivationLink}

            Ce lien est valable {FrenchDurations.Write(EmailTokenValidityDurations.TwoFactorDeactivationLink)} et ne sert qu'une seule fois. Il ne marche pas si ton mot de passe a changé il y a moins de 7 jours, et 5 mauvais mots de passe l'annulent. Désactiver la double authentification ferme toutes les sessions de ton compte.

            Si tu n'as rien demandé au support, ignore ce message : sans le mot de passe du compte, ce lien ne change rien. Écris aussi au support de WalletFlow :
            {supportPageAddress}
            """;

        return new EmailMessage(recipient, "Désactive la double authentification de ton compte WalletFlow", body);
    }
}
