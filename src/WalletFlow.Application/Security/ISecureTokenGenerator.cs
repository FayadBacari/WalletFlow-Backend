namespace WalletFlow.Application.Security;

// Création des jetons des liens envoyés par e-mail (SS §2). Implémenté dans la couche Infrastructure.
public interface ISecureTokenGenerator
{
    // Tire au hasard un nouveau jeton et calcule son empreinte.
    GeneratedSecureToken GenerateNewRandomSecureTokenWithItsHash();

    // Calcule l'empreinte d'un jeton reçu dans un lien, pour le retrouver en base.
    string ConvertSecureTokenIntoItsHash(string tokenToConvertIntoItsHash);
}
