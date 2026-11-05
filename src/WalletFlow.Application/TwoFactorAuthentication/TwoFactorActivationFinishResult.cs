namespace WalletFlow.Application.TwoFactorAuthentication;

// Résultat de la fin d'une activation : les codes de secours à donner une seule fois, ou le refus à afficher.
// Une classe plutôt qu'un record : un record afficherait les codes si on l'écrivait dans un journal.
public class TwoFactorActivationFinishResult
{
    public string? WrongCodeErrorMessage { get; init; }
    public string? ActivationMustRestartErrorMessage { get; init; }
    public List<string>? BackupCodesToGiveOnlyOnce { get; init; }

    public bool IsTwoFactorActivated => BackupCodesToGiveOnlyOnce != null;
}
