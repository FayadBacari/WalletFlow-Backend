using WalletFlow.Application.Emails;

namespace WalletFlow.Application.AccountSettings;

// E-mails du changement d'adresse et de la suppression du compte. L'Application choisit l'e-mail et ses données ;
// l'Infrastructure en écrit le texte.
// Ils ne contiennent jamais de mot de passe, et disent quoi faire si ce n'est pas l'utilisateur qui a agi (SS §9).
public interface IAccountChangeEmails
{
    // Crée l'e-mail envoyé à la nouvelle adresse, avec le lien qui la confirme (GR Compte).
    EmailMessage CreateEmailChangeLinkEmail(string recipient, string confirmationLink);

    // Crée l'e-mail qui prévient le propriétaire d'une adresse déjà utilisée qu'un autre compte a demandé à la prendre (SS §2).
    EmailMessage CreateAddressAlreadyUsedEmail(string recipient);

    // Crée l'e-mail qui prévient l'ancienne adresse que le changement est fait. Il ne cite jamais la nouvelle, et donne
    // l'adresse de la page de support (SS §9).
    EmailMessage CreateEmailChangedEmail(string recipient, string supportPageAddress);

    // Crée l'e-mail qui confirme la demande de suppression, avec le jour de Paris où le compte sera effacé, et la façon de
    // changer d'avis (GR Suppression).
    EmailMessage CreateDeletionRequestedEmail(string recipient, DateOnly erasureDayInParis, string supportPageAddress);

    // Crée l'e-mail qui confirme l'effacement définitif, envoyé à l'adresse du compte effacé (GR Suppression).
    EmailMessage CreateAccountErasedEmail(string recipient);
}
