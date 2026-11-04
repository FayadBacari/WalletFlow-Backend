using WalletFlow.Application.Emails;

namespace WalletFlow.Application.UserAccounts;

// Contenu des e-mails de l'inscription. Ils ne contiennent jamais de mot de passe (SS §9).
public static class AccountRegistrationEmails
{
    // Crée l'e-mail qui contient le lien de confirmation de l'inscription (GR Auth).
    public static EmailMessageToSend CreateSignupConfirmationEmailWithItsLink(
        string recipientEmailAddressInStoredForm,
        string signupConfirmationLinkForTheRecipient)
    {
        var bodyOfTheSignupConfirmationEmail = $"""
            Bonjour,

            Pour activer ton compte WalletFlow, ouvre ce lien puis saisis le mot de passe choisi à l'inscription :
            {signupConfirmationLinkForTheRecipient}

            Ce lien est valable 24 heures et ne sert qu'une seule fois.

            Si tu n'es pas à l'origine de cette inscription, ignore ce message : aucun compte ne sera activé.
            """;

        return new EmailMessageToSend(
            recipientEmailAddressInStoredForm,
            "Confirme ton adresse e-mail WalletFlow",
            bodyOfTheSignupConfirmationEmail);
    }

    // Crée l'e-mail qui prévient le propriétaire d'une adresse déjà utilisée (SS §2).
    public static EmailMessageToSend CreateWarningEmailForAddressAlreadyUsed(string recipientEmailAddressInStoredForm)
    {
        var bodyOfTheAddressAlreadyUsedWarning = """
            Bonjour,

            Quelqu'un vient d'essayer de créer un compte WalletFlow avec cette adresse, qui en a déjà un. Aucun nouveau compte n'a été créé.

            Si c'est toi et que tu ne retrouves plus ton mot de passe, utilise « Mot de passe oublié » sur la page de connexion.

            Si ce n'est pas toi, tu n'as rien à faire : ton compte n'a pas changé.
            """;

        return new EmailMessageToSend(
            recipientEmailAddressInStoredForm,
            "Tentative d'inscription avec ton adresse e-mail",
            bodyOfTheAddressAlreadyUsedWarning);
    }
}
