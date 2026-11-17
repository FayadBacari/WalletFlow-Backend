using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit l'e-mail d'une demande de la file en code de la colonne type_demande_email, et inversement.
public static class EmailRequestTypeDatabaseCodes
{
    // Code de la colonne type_demande_email pour chaque valeur de la liste.
    public static readonly EnumDatabaseCodes<EmailRequestType> Table =
        new EnumDatabaseCodes<EmailRequestType>(
            new Dictionary<EmailRequestType, string>
            {
                { EmailRequestType.PasswordRecovery, "recuperation_mdp" },
                { EmailRequestType.AccountUnlock, "deblocage" },
                { EmailRequestType.EmailChange, "changement_email" },
                { EmailRequestType.EmailChangedNotice, "avis_ancienne_adresse" },
                { EmailRequestType.NewDevice, "nouvel_appareil" },
                { EmailRequestType.PasswordChange, "changement_mdp" },
                { EmailRequestType.TwoFactorDeactivation, "desactivation_double_auth" },
                { EmailRequestType.BackupCodeUse, "utilisation_code_secours" },
                { EmailRequestType.TwoFactorDeactivationLink, "lien_double_auth" },
                { EmailRequestType.SupportResponse, "reponse_support" },
                { EmailRequestType.Alert, "alerte" },
                { EmailRequestType.DeletionRequest, "demande_suppression" },
                { EmailRequestType.DeletionCancellation, "annulation_suppression" },
                { EmailRequestType.AccountErasure, "effacement_compte" },
            });
}
