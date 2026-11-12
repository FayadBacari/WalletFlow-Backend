using WalletFlow.Application.AccountSettings;
using WalletFlow.Application.Emails;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Emails;

// Texte en français des e-mails du changement d'adresse. La durée du lien vient de sa règle : le texte la suit si elle change.
public class FrenchAccountChangeEmails : IAccountChangeEmails
{
    // Crée l'e-mail envoyé à la nouvelle adresse, avec le lien qui la confirme (GR Compte).
    public EmailMessage CreateEmailChangeLinkEmail(string recipient, string confirmationLink)
    {
        var body = $"""
            Bonjour,

            Pour que cette adresse devienne celle de ton compte WalletFlow, ouvre ce lien, puis saisis ton mot de passe :
            {confirmationLink}

            Ce lien est valable {FrenchDurations.Write(EmailTokenValidityDurations.EmailChangeLink)} et ne sert qu'une seule fois. D'ici là, ton ancienne adresse reste celle de ton compte.

            Si tu n'es pas à l'origine de cette demande, ignore ce message : sans le mot de passe du compte, ce lien ne change rien.
            """;

        return new EmailMessage(recipient, "Confirme ta nouvelle adresse e-mail WalletFlow", body);
    }

    // Crée l'e-mail qui prévient le propriétaire d'une adresse déjà utilisée qu'un autre compte a demandé à la prendre (SS §2).
    public EmailMessage CreateAddressAlreadyUsedEmail(string recipient)
    {
        var body = """
            Bonjour,

            Quelqu'un a demandé à utiliser cette adresse pour un autre compte WalletFlow. Elle est déjà utilisée par un compte : rien n'a changé.

            Si c'est toi, connecte-toi plutôt avec cette adresse. Sinon, ignore ce message.
            """;

        return new EmailMessage(recipient, "Ton adresse e-mail est déjà utilisée sur WalletFlow", body);
    }

    // Crée l'e-mail qui prévient l'ancienne adresse que le changement est fait. Il ne cite jamais la nouvelle (SS §9).
    public EmailMessage CreateEmailChangedEmail(string recipient)
    {
        var body = """
            Bonjour,

            L'adresse e-mail de ton compte WalletFlow vient d'être changée : cette adresse ne sert plus à te connecter.

            Si ce n'est pas toi qui as fait ce changement, écris tout de suite au support de WalletFlow.
            """;

        return new EmailMessage(recipient, "L'adresse e-mail de ton compte WalletFlow a changé", body);
    }
}
