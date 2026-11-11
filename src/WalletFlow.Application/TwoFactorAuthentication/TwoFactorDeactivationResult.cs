namespace WalletFlow.Application.TwoFactorAuthentication;

// Résultat d'une désactivation : une seule de ces valeurs à la fois.
// Un mauvais mot de passe et un mauvais code donnent la même valeur :
// une session volée ne peut pas servir à tester des mots de passe un par un.
public enum TwoFactorDeactivationResult
{
    TwoFactorDeactivated,
    TwoFactorNotEnabled,
    WrongPasswordOrCode
}
