using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit la nature d'un événement du journal en code de la colonne type_evenement, et inversement.
public static class SecurityEventTypeDatabaseCodes
{
    // Convertisseur utilisé par EF Core pour lire et écrire la colonne type_evenement.
    public static readonly ValueConverter<SecurityEventType, string> ConverterBetweenSecurityEventTypeAndDatabaseCode =
        new ValueConverter<SecurityEventType, string>(
            securityEventTypeToWriteIntoTheDatabase => ConvertSecurityEventTypeIntoItsDatabaseCode(securityEventTypeToWriteIntoTheDatabase),
            databaseCodeReadForTheSecurityEventType => ConvertDatabaseCodeIntoItsSecurityEventType(databaseCodeReadForTheSecurityEventType));

    // Donne le code enregistré en base pour une nature d'événement.
    public static string ConvertSecurityEventTypeIntoItsDatabaseCode(SecurityEventType securityEventTypeToConvertIntoDatabaseCode)
    {
        switch (securityEventTypeToConvertIntoDatabaseCode)
        {
            case SecurityEventType.Login:
                return "connexion";
            case SecurityEventType.Lockout:
                return "blocage";
            case SecurityEventType.Unlock:
                return "deblocage";
            case SecurityEventType.PasswordRecovery:
                return "recuperation_mdp";
            case SecurityEventType.PasswordChange:
                return "changement_mdp";
            case SecurityEventType.EmailChange:
                return "changement_email";
            case SecurityEventType.TwoFactorActivation:
                return "activation_double_auth";
            case SecurityEventType.BankConnection:
                return "connexion_banque";
            case SecurityEventType.BankDisconnection:
                return "deconnexion_banque";
            case SecurityEventType.DataExport:
                return "export_donnees";
            case SecurityEventType.DeletionRequest:
                return "demande_suppression";
            case SecurityEventType.AdminAction:
                return "action_admin";
            case SecurityEventType.RoleChange:
                return "changement_role";
            case SecurityEventType.AccessDenied:
                return "acces_refuse";
            default:
                throw new ArgumentOutOfRangeException(nameof(securityEventTypeToConvertIntoDatabaseCode));
        }
    }

    // Donne la nature d'événement qui correspond à un code lu en base.
    public static SecurityEventType ConvertDatabaseCodeIntoItsSecurityEventType(string databaseCodeToConvertIntoSecurityEventType)
    {
        switch (databaseCodeToConvertIntoSecurityEventType)
        {
            case "connexion":
                return SecurityEventType.Login;
            case "blocage":
                return SecurityEventType.Lockout;
            case "deblocage":
                return SecurityEventType.Unlock;
            case "recuperation_mdp":
                return SecurityEventType.PasswordRecovery;
            case "changement_mdp":
                return SecurityEventType.PasswordChange;
            case "changement_email":
                return SecurityEventType.EmailChange;
            case "activation_double_auth":
                return SecurityEventType.TwoFactorActivation;
            case "connexion_banque":
                return SecurityEventType.BankConnection;
            case "deconnexion_banque":
                return SecurityEventType.BankDisconnection;
            case "export_donnees":
                return SecurityEventType.DataExport;
            case "demande_suppression":
                return SecurityEventType.DeletionRequest;
            case "action_admin":
                return SecurityEventType.AdminAction;
            case "changement_role":
                return SecurityEventType.RoleChange;
            case "acces_refuse":
                return SecurityEventType.AccessDenied;
            default:
                throw new ArgumentOutOfRangeException(nameof(databaseCodeToConvertIntoSecurityEventType));
        }
    }
}
