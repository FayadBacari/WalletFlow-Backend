using WalletFlow.Application.Emails;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Emails;

// Texte en français des e-mails de l'inscription. La durée du lien vient de sa règle : le texte la suit si elle change.
public class FrenchAccountRegistrationEmails : IAccountRegistrationEmails
{
    // Crée l'e-mail qui contient le lien de confirmation de l'inscription (GR Auth).
    public EmailMessage CreateConfirmationEmail(string recipient, string confirmationLink)
    {
        var body = $"""
            Bonjour,

            Pour activer ton compte WalletFlow, ouvre ce lien puis saisis le mot de passe choisi à l'inscription :
            {confirmationLink}

            Ce lien est valable {FrenchDurations.Write(EmailTokenValidityDurations.SignupConfirmationLink)} et ne sert qu'une seule fois.

            Si tu n'es pas à l'origine de cette inscription, ignore ce message : aucun compte ne sera activé.
            """;

        return new EmailMessage(recipient, "Confirme ton adresse e-mail WalletFlow", body);
    }

    // Crée l'e-mail qui prévient le propriétaire d'une adresse déjà utilisée (SS §2).
    public EmailMessage CreateAddressAlreadyUsedEmail(string recipient)
    {
        var body = """
            Bonjour,

            Quelqu'un vient d'essayer de créer un compte WalletFlow avec cette adresse, qui en a déjà un. Aucun nouveau compte n'a été créé.

            Si c'est toi et que tu ne retrouves plus ton mot de passe, utilise « Mot de passe oublié » sur la page de connexion.

            Si ce n'est pas toi, tu n'as rien à faire : ton compte n'a pas changé.
            """;

        return new EmailMessage(recipient, "Tentative d'inscription avec ton adresse e-mail", body);
    }
}
