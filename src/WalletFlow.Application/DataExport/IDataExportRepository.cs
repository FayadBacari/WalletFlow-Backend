namespace WalletFlow.Application.DataExport;

// Lit les données d'un compte pour le fichier de ses données. Implémenté dans la couche Infrastructure.
public interface IDataExportRepository
{
    // Lit toutes les données que l'utilisateur voit, au même instant de la base : une modification enregistrée pendant la
    // lecture n'y apparaît pas à moitié. Renvoie null si le compte n'existe plus.
    Task<AccountData?> ReadAsync(Guid userId);
}
