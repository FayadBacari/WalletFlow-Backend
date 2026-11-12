using WalletFlow.Application.Emails;

namespace WalletFlow.Application.AccountSettings;

// E-mails du changement d'adresse. L'Application choisit l'e-mail et ses données ; l'Infrastructure en écrit le texte.
// Ils ne contiennent jamais de mot de passe, et disent quoi faire si ce n'est pas l'utilisateur qui a agi (SS §9).
public interface IAccountChangeEmails
{
    // Crée l'e-mail envoyé à la nouvelle adresse, avec le lien qui la confirme (GR Compte).
    EmailMessage CreateEmailChangeLinkEmail(string recipient, string confirmationLink);

    // Crée l'e-mail qui prévient le propriétaire d'une adresse déjà utilisée qu'un autre compte a demandé à la prendre (SS §2).
    EmailMessage CreateAddressAlreadyUsedEmail(string recipient);

    // Crée l'e-mail qui prévient l'ancienne adresse que le changement est fait. Il ne cite jamais la nouvelle (SS §9).
    EmailMessage CreateEmailChangedEmail(string recipient);
}
