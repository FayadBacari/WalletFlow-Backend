namespace WalletFlow.Application.TwoFactorAuthentication;

// Chiffrement du secret de double authentification avant son passage en base (SS §2 et §7). Implémenté dans la couche Infrastructure.
public interface ITotpSecretProtector
{
    // Chiffre le secret de ce compte. Le résultat seul va en base.
    string Encrypt(string secret, Guid accountId);

    // Déchiffre le secret de ce compte. Lève une exception si la clé a changé, si le texte a été modifié,
    // ou s'il appartient à un autre compte : le secret n'est jamais deviné.
    string Decrypt(string encryptedSecret, Guid accountId);

    // Le début des textes chiffrés avec la clé actuelle : les autres attendent d'être rechiffrés avec elle (SS §7).
    string CurrentKeyPrefix { get; }

    // Renvoie vrai si ce texte est chiffré avec la clé actuelle.
    bool UsesCurrentKey(string encryptedSecret);
}
