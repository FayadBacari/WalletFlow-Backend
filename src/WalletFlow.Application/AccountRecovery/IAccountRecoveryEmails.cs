using WalletFlow.Application.Emails;

namespace WalletFlow.Application.AccountRecovery;

// E-mails de récupération et de déblocage. L'Application choisit l'e-mail et ses données ; l'Infrastructure en écrit le texte.
// Ils ne contiennent jamais de mot de passe, et disent quoi faire si ce n'est pas l'utilisateur qui a agi (SS §9).
public interface IAccountRecoveryEmails
{
    // Crée l'e-mail qui contient le lien de récupération du mot de passe (GR Auth).
    EmailMessage CreateRecoveryEmail(string recipient, string recoveryLink);

    // Crée l'e-mail qui invite à s'inscrire à nouveau : une adresse non confirmée n'a pas de lien de récupération (GR Auth).
    EmailMessage CreateSignupInvitationEmail(string recipient);

    // Crée l'e-mail qui prévient du blocage et de sa durée, avec le lien de déblocage (GR Auth, SS §2).
    EmailMessage CreateUnlockEmail(string recipient, string unlockLink, TimeSpan lockoutDuration);
}
