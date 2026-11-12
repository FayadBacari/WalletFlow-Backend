namespace WalletFlow.Application.BankConnections;

// Résultat d'une mise à jour demandée par l'utilisateur : toujours une seule des formes écrites dans ce fichier.
public abstract class BankUpdateResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private BankUpdateResult()
    {
    }

    // Chaque connexion a été mise à jour, ou marquée en erreur ou expirée si sa banque n'a pas répondu.
    public sealed class Updated : BankUpdateResult
    {
    }

    // L'utilisateur a déjà demandé une mise à jour dans la dernière minute (SS §6).
    public sealed class TooManyUpdates : BankUpdateResult
    {
    }
}
