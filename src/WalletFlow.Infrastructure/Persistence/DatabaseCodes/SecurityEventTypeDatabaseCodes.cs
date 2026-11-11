using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit la nature d'un événement du journal en code de la colonne type_evenement, et inversement.
public static class SecurityEventTypeDatabaseCodes
{
    // Code de la colonne type_evenement pour chaque valeur de la liste.
    public static readonly EnumDatabaseCodes<SecurityEventType> Table =
        new EnumDatabaseCodes<SecurityEventType>(
            new Dictionary<SecurityEventType, string>
            {
                { SecurityEventType.Login, "connexion" },
                { SecurityEventType.Lockout, "blocage" },
                { SecurityEventType.Unlock, "deblocage" },
                { SecurityEventType.PasswordRecovery, "recuperation_mdp" },
                { SecurityEventType.PasswordChange, "changement_mdp" },
                { SecurityEventType.EmailChange, "changement_email" },
                { SecurityEventType.TwoFactorActivation, "activation_double_auth" },
                { SecurityEventType.TwoFactorDeactivation, "desactivation_double_auth" },
                { SecurityEventType.BankConnection, "connexion_banque" },
                { SecurityEventType.BankDisconnection, "deconnexion_banque" },
                { SecurityEventType.DataExport, "export_donnees" },
                { SecurityEventType.DeletionRequest, "demande_suppression" },
                { SecurityEventType.AdminAction, "action_admin" },
                { SecurityEventType.RoleChange, "changement_role" },
                { SecurityEventType.AccessDenied, "acces_refuse" },
            });
}
