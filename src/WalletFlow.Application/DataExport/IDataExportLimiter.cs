namespace WalletFlow.Application.DataExport;

// Limite les téléchargements des données d'un utilisateur : 3 par heure (SS §6). Implémenté dans la couche Infrastructure.
public interface IDataExportLimiter
{
    // Compte un essai de plus pour cet utilisateur. Renvoie faux s'il en a déjà fait 3 dans l'heure.
    bool TryCountExport(Guid userId);
}
