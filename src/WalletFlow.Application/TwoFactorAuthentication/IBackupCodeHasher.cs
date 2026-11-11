namespace WalletFlow.Application.TwoFactorAuthentication;

// Hachage des codes de secours, comme un mot de passe (SS §2). Implémenté dans la couche Infrastructure.
// Les codes d'un même compte ont le même sel : un code tapé ne demande donc qu'un seul calcul,
// dont l'empreinte est ensuite comparée à celle de chaque code enregistré.
public interface IBackupCodeHasher
{
    // Transforme un code de secours en empreinte. Le même code, pour le même compte, donne toujours la même empreinte.
    Task<string> HashAsync(string cleanedCode, Guid accountId);
}
