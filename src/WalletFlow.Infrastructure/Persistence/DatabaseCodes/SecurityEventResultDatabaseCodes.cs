using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.DatabaseCodes;

// Traduit le résultat d'un événement du journal en code de la colonne resultat_evenement, et inversement.
public static class SecurityEventResultDatabaseCodes
{
    // Code de la colonne resultat_evenement pour chaque valeur de la liste.
    public static readonly EnumDatabaseCodes<SecurityEventResult> Table =
        new EnumDatabaseCodes<SecurityEventResult>(
            new Dictionary<SecurityEventResult, string>
            {
                { SecurityEventResult.Success, "succes" },
                { SecurityEventResult.Failure, "echec" },
            });
}
