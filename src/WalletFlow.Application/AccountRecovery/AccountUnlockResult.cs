namespace WalletFlow.Application.AccountRecovery;

// Résultat d'un clic sur le lien de déblocage : toujours une seule des formes écrites dans ce fichier.
public abstract class AccountUnlockResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private AccountUnlockResult()
    {
    }

    // Le compte est débloqué, avec ses essais remis à zéro.
    public sealed class AccountUnlocked : AccountUnlockResult
    {
    }

    // Le lien n'existe pas, a déjà servi, a expiré ou a été remplacé.
    public sealed class LinkNoLongerValid : AccountUnlockResult
    {
    }
}
