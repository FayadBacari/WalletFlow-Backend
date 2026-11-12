namespace WalletFlow.Application.AccountSettings;

// Résultat d'un changement de mot de passe : toujours une seule des formes écrites dans ce fichier.
public abstract class PasswordChangeResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private PasswordChangeResult()
    {
    }

    // Le mot de passe est changé, toutes les sessions sont fermées, et la session en cours continue avec ce nouvel identifiant (SS §3).
    public sealed class Changed : PasswordChangeResult
    {
        public required string SessionToken { get; init; }
    }

    // Le nouveau mot de passe ne respecte pas les règles : rien n'est vérifié ni changé.
    public sealed class NewPasswordBreaksRules : PasswordChangeResult
    {
    }

    // Le mot de passe actuel n'est pas le bon, sur un compte sans double authentification.
    public sealed class WrongPassword : PasswordChangeResult
    {
    }

    // Le mot de passe actuel ou le code n'est pas bon, sur un compte avec double authentification : le même refus pour les deux.
    public sealed class WrongPasswordOrCode : PasswordChangeResult
    {
    }
}
