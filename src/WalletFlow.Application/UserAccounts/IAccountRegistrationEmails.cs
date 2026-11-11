using WalletFlow.Application.Emails;

namespace WalletFlow.Application.UserAccounts;

// E-mails de l'inscription. L'Application choisit l'e-mail et ses données ; l'Infrastructure en écrit le texte.
// Un e-mail ne contient jamais de mot de passe (SS §9).
public interface IAccountRegistrationEmails
{
    // Crée l'e-mail qui contient le lien de confirmation de l'inscription (GR Auth).
    EmailMessage CreateConfirmationEmail(string recipient, string confirmationLink);

    // Crée l'e-mail qui prévient le propriétaire d'une adresse déjà utilisée (SS §2).
    EmailMessage CreateAddressAlreadyUsedEmail(string recipient);
}
