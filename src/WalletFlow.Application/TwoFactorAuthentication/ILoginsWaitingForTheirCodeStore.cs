namespace WalletFlow.Application.TwoFactorAuthentication;

// Les connexions qui attendent leur code, 5 minutes au plus (SS §2). Implémenté dans la couche Infrastructure.
public interface ILoginsWaitingForTheirCodeStore
{
    // Garde une nouvelle connexion en attente. Seule l'empreinte de son jeton sert à la retrouver.
    void SaveNewLoginWaitingForItsCode(string hashOfTheTokenOfTheWaitingLogin, Guid identifierOfTheAccountLoggingIn, string secureHashOfThePasswordJustChecked);

    // Compte un essai de code de plus et donne la connexion, avant même de vérifier le code :
    // des requêtes simultanées ne peuvent donc pas tester plus de 5 codes (SS §6).
    // Renvoie null si la connexion n'existe pas, si elle a expiré, ou si ses 5 essais sont déjà pris.
    LoginWaitingForItsCode? TryToCountOneCodeAttemptOfThisWaitingLogin(string hashOfTheTokenOfTheWaitingLogin);

    // Oublie une connexion en attente. Ne fait rien si elle n'existe plus.
    void RemoveThisLoginWaitingForItsCode(string hashOfTheTokenOfTheWaitingLogin);
}
