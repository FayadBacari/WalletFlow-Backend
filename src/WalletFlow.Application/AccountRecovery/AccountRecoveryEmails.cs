using WalletFlow.Application.Emails;

namespace WalletFlow.Application.AccountRecovery;

// Contenu des e-mails de récupération et de déblocage. Ils ne contiennent jamais de mot de passe,
// et disent quoi faire si ce n'est pas l'utilisateur qui a agi (SS §9).
public static class AccountRecoveryEmails
{
    // Crée l'e-mail qui contient le lien de récupération du mot de passe (GR Auth).
    public static EmailMessageToSend CreatePasswordRecoveryEmailWithItsLink(string recipientEmailAddressInStoredForm, string passwordRecoveryLinkForTheRecipient)
    {
        var bodyOfThePasswordRecoveryEmail = $"""
            Bonjour,

            Pour choisir un nouveau mot de passe WalletFlow, ouvre ce lien :
            {passwordRecoveryLinkForTheRecipient}

            Ce lien est valable 30 minutes et ne sert qu'une seule fois.

            Si tu n'es pas à l'origine de cette demande, ignore ce message : ton mot de passe ne change pas.
            """;

        return new EmailMessageToSend(recipientEmailAddressInStoredForm, "Choisis un nouveau mot de passe WalletFlow", bodyOfThePasswordRecoveryEmail);
    }

    // Crée l'e-mail qui invite à s'inscrire à nouveau : une adresse non confirmée n'a pas de lien de récupération (GR Auth).
    public static EmailMessageToSend CreateInvitationEmailForAddressNotYetConfirmed(string recipientEmailAddressInStoredForm)
    {
        var bodyOfTheInvitationEmailToRegister = """
            Bonjour,

            Tu as demandé à retrouver ton mot de passe WalletFlow, mais cette adresse n'est pas encore confirmée.

            Pour continuer, inscris-toi à nouveau avec cette adresse : tu choisiras un nouveau mot de passe et tu recevras un nouveau lien de confirmation.

            Si tu n'es pas à l'origine de cette demande, ignore ce message : rien n'a changé.
            """;

        return new EmailMessageToSend(recipientEmailAddressInStoredForm, "Ton adresse e-mail WalletFlow n'est pas encore confirmée", bodyOfTheInvitationEmailToRegister);
    }

    // Crée l'e-mail qui prévient du blocage et contient le lien de déblocage (GR Auth, SS §2).
    public static EmailMessageToSend CreateAccountUnlockEmailWithItsLink(
        string recipientEmailAddressInStoredForm,
        string accountUnlockLinkForTheRecipient,
        TimeSpan durationOfTheCurrentLockoutOfTheAccount)
    {
        var bodyOfTheAccountUnlockEmail = $"""
            Bonjour,

            Plusieurs essais de connexion ont échoué : ton compte WalletFlow est bloqué pendant {WriteDurationOfTheLockoutInFrenchWords(durationOfTheCurrentLockoutOfTheAccount)}.

            Pour le débloquer sans attendre, ouvre ce lien :
            {accountUnlockLinkForTheRecipient}

            Ce lien est valable 1 heure et ne sert qu'une seule fois. Il ne te connecte pas : tu devras ensuite saisir ton mot de passe.

            Si ce n'est pas toi qui as essayé de te connecter, ton mot de passe n'a pas changé. Par précaution, tu peux en choisir un nouveau avec « Mot de passe oublié ».
            """;

        return new EmailMessageToSend(recipientEmailAddressInStoredForm, "Ton compte WalletFlow est bloqué", bodyOfTheAccountUnlockEmail);
    }

    // Écrit une durée de blocage en français.
    private static string WriteDurationOfTheLockoutInFrenchWords(TimeSpan durationOfTheCurrentLockoutOfTheAccount)
    {
        if (durationOfTheCurrentLockoutOfTheAccount < TimeSpan.FromHours(1))
        {
            return (int)durationOfTheCurrentLockoutOfTheAccount.TotalMinutes + " minutes";
        }

        if (durationOfTheCurrentLockoutOfTheAccount == TimeSpan.FromHours(1))
        {
            return "1 heure";
        }

        return (int)durationOfTheCurrentLockoutOfTheAccount.TotalHours + " heures";
    }
}
