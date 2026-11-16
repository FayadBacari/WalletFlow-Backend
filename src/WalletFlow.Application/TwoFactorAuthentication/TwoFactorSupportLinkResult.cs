namespace WalletFlow.Application.TwoFactorAuthentication;

// Résultat du lien du support qui désactive la double authentification : toujours une seule des formes écrites ici.
public abstract class TwoFactorSupportLinkResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private TwoFactorSupportLinkResult()
    {
    }

    // La double authentification est désactivée, et toutes les sessions du compte sont fermées.
    public sealed class Deactivated : TwoFactorSupportLinkResult
    {
    }

    // Le mot de passe n'est pas le bon. Il reste au lien au moins un essai.
    public sealed class WrongPassword : TwoFactorSupportLinkResult
    {
    }

    // Le mot de passe n'est pas le bon, et c'était le 5e essai : le lien est annulé (GR Auth).
    public sealed class LinkCancelled : TwoFactorSupportLinkResult
    {
    }

    // Le mot de passe du compte a changé il y a moins de 7 jours : le lien ne marche pas (GR Auth). Aucun essai n'est compté.
    public sealed class PasswordChangedRecently : TwoFactorSupportLinkResult
    {
    }

    // Le lien n'existe pas, a déjà servi, a expiré, n'a plus d'essai, ou la double authentification est déjà désactivée.
    public sealed class LinkNoLongerValid : TwoFactorSupportLinkResult
    {
    }
}
