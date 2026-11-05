namespace WalletFlow.Application.TwoFactorAuthentication;

// Résultat de la vérification d'un code : valable ou non et, s'il s'agit d'un code de secours, lequel.
public class TwoFactorCodeCheckResult
{
    public required bool IsCodeValid { get; init; }
    public Guid? IdentifierOfTheBackupCodeUsed { get; init; }
}
