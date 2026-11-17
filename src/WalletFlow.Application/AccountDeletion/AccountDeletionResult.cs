namespace WalletFlow.Application.AccountDeletion;

// Résultat d'une demande de suppression : toujours une seule des formes écrites dans ce fichier.
public abstract class AccountDeletionResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private AccountDeletionResult()
    {
    }

    // La suppression est demandée : le compte sera effacé à ce moment, sauf si l'utilisateur se reconnecte avant.
    public sealed class Requested : AccountDeletionResult
    {
        public required DateTime ErasesAt { get; init; }
    }

    // Le mot de passe n'est pas le bon, sur un compte sans double authentification.
    public sealed class WrongPassword : AccountDeletionResult
    {
    }

    // Le mot de passe ou le code n'est pas le bon, avec la double authentification : le même refus pour les deux.
    public sealed class WrongPasswordOrCode : AccountDeletionResult
    {
    }

    // Le compte n'existe plus.
    public sealed class AccountGone : AccountDeletionResult
    {
    }
}
