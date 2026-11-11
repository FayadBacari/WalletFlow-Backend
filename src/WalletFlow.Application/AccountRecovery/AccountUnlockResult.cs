namespace WalletFlow.Application.AccountRecovery;

// Résultat d'un clic sur le lien de déblocage : une seule de ces valeurs à la fois.
public enum AccountUnlockResult
{
    AccountUnlocked,
    LinkNoLongerValid
}
