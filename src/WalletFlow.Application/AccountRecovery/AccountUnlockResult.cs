namespace WalletFlow.Application.AccountRecovery;

// Résultat d'un clic sur le lien de déblocage : accepté, ou refusé avec la raison du refus.
public class AccountUnlockResult
{
    public string? LinkErrorMessage { get; init; }

    public bool IsUnlocked => LinkErrorMessage == null;
}
