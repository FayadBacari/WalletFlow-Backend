namespace WalletFlow.Application.AccountRecovery;

// Résultat du choix d'un nouveau mot de passe : une seule de ces valeurs à la fois.
public enum PasswordResetResult
{
    PasswordChanged,
    LinkNoLongerValid,
    PasswordBreaksRules
}
