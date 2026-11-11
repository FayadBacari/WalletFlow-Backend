using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit l'état d'une connexion bancaire en code de la colonne statut_connexion, et inversement.
public static class BankConnectionStatusDatabaseCodes
{
    // Code de la colonne statut_connexion pour chaque valeur de la liste.
    public static readonly EnumDatabaseCodes<BankConnectionStatus> Table =
        new EnumDatabaseCodes<BankConnectionStatus>(
            new Dictionary<BankConnectionStatus, string>
            {
                { BankConnectionStatus.Active, "active" },
                { BankConnectionStatus.InError, "en_erreur" },
                { BankConnectionStatus.Expired, "expiree" },
            });
}
