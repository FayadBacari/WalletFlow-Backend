namespace WalletFlow.Application.Security;

// Création des jetons aléatoires : liens envoyés par e-mail, sessions et appareils (SS §2 et §3). Implémenté dans la couche Infrastructure.
public interface ISecureTokenGenerator
{
    // Tire au hasard un nouveau jeton et calcule son empreinte.
    GeneratedSecureToken GenerateNewRandomSecureTokenWithItsHash();

    // Calcule l'empreinte d'un jeton reçu dans un lien ou un cookie, pour le retrouver en base.
    string ConvertSecureTokenIntoItsHash(string tokenToConvertIntoItsHash);
}
