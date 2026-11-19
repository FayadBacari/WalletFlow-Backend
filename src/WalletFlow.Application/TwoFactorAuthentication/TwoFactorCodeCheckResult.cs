namespace WalletFlow.Application.TwoFactorAuthentication;

// Résultat de la vérification d'un code : valable ou non et, s'il s'agit d'un code de secours, lequel. Pour un code de
// l'application, son pas de temps : le code n'est dépensé qu'avec les autres changements de l'action qu'il autorise.
public class TwoFactorCodeCheckResult
{
    public required bool IsCodeValid { get; init; }
    public Guid? BackupCodeId { get; init; }
    public long? AuthenticatorStep { get; init; }
}
