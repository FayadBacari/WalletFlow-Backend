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
                { EmailRequestType.Alert, "alerte" },
            });
}
