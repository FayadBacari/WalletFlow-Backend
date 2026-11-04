namespace WalletFlow.Application.Security;

// Hachage des mots de passe (SS §2). Implémenté dans la couche Infrastructure.
public interface IPasswordHasher
{
    // Transforme un mot de passe en empreinte, seule valeur enregistrée en base.
    string ConvertPasswordIntoItsSecureHash(string passwordToConvertIntoSecureHash);

    // Renvoie vrai si le mot de passe correspond à l'empreinte enregistrée en base.
    bool CheckThatPasswordMatchesItsSecureHash(string passwordToCompareWithItsHash, string secureHashStoredInTheDatabase);
}
