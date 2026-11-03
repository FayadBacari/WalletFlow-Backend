using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit l'état d'une connexion bancaire en code de la colonne statut_connexion, et inversement.
public static class BankConnectionStatusDatabaseCodes
{
    // Convertisseur utilisé par EF Core pour lire et écrire la colonne statut_connexion.
    public static readonly ValueConverter<BankConnectionStatus, string> ConverterBetweenBankConnectionStatusAndDatabaseCode =
        new ValueConverter<BankConnectionStatus, string>(
            bankConnectionStatusToWriteIntoTheDatabase => ConvertBankConnectionStatusIntoItsDatabaseCode(bankConnectionStatusToWriteIntoTheDatabase),
            databaseCodeReadForTheBankConnectionStatus => ConvertDatabaseCodeIntoItsBankConnectionStatus(databaseCodeReadForTheBankConnectionStatus));

    // Donne le code enregistré en base pour un état de connexion.
    public static string ConvertBankConnectionStatusIntoItsDatabaseCode(BankConnectionStatus bankConnectionStatusToConvertIntoDatabaseCode)
    {
        switch (bankConnectionStatusToConvertIntoDatabaseCode)
        {
            case BankConnectionStatus.Active:
                return "active";
            case BankConnectionStatus.InError:
                return "en_erreur";
            case BankConnectionStatus.Expired:
                return "expiree";
            default:
                throw new ArgumentOutOfRangeException(nameof(bankConnectionStatusToConvertIntoDatabaseCode));
        }
    }

    // Donne l'état de connexion qui correspond à un code lu en base.
    public static BankConnectionStatus ConvertDatabaseCodeIntoItsBankConnectionStatus(string databaseCodeToConvertIntoBankConnectionStatus)
    {
        switch (databaseCodeToConvertIntoBankConnectionStatus)
        {
            case "active":
                return BankConnectionStatus.Active;
            case "en_erreur":
                return BankConnectionStatus.InError;
            case "expiree":
                return BankConnectionStatus.Expired;
            default:
                throw new ArgumentOutOfRangeException(nameof(databaseCodeToConvertIntoBankConnectionStatus));
        }
    }
}
