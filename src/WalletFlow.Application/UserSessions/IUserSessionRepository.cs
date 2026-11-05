using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserSessions;

// Accès à la base pour les sessions et les appareils. Implémenté dans la couche Infrastructure.
public interface IUserSessionRepository
{
    // Enregistre une connexion réussie en une seule transaction SQL : les compteurs du compte, la nouvelle session,
    // le nouvel appareil s'il y en a un, le code de secours utilisé s'il y en a un, et l'événement du journal.
    // Renvoie faux si une autre requête vient de modifier le compte ou d'utiliser ce code : rien n'est alors enregistré.
    Task<bool> TryToSaveSuccessfulLoginOfThisAccountAsync(
        User accountThatHasJustLoggedIn,
        UserSession newSessionOfTheUserAccount,
        Device? newDeviceOfTheUserAccount,
        BackupCode? backupCodeUsedForThisLogin,
        SecurityEvent securityEventOfTheSuccessfulLogin);

    // Renvoie vrai si cette empreinte de jeton d'appareil appartient à ce compte.
    Task<bool> CheckThatDeviceTokenBelongsToThisAccountAsync(string hashOfTheDeviceTokenFromCookie, Guid identifierOfTheAccountLoggingIn);

    // Cherche une session par l'empreinte de son identifiant. Renvoie null si elle n'existe pas.
    Task<UserSession?> FindUserSessionByItsHashAsync(string hashOfTheTokenOfTheSession);

    // Note que la session vient de servir.
    Task RecordActivityOfThisUserSessionAsync(string hashOfTheTokenOfTheSession, DateTime dateOfTheCurrentHttpRequest);

    // Détruit une session. Ne fait rien si elle n'existe plus.
    Task DeleteUserSessionByItsHashAsync(string hashOfTheTokenOfTheSession);

    // Détruit toutes les sessions d'un compte, après une récupération de mot de passe (SS §3).
    Task DeleteAllUserSessionsOfThisAccountAsync(Guid identifierOfTheAccountToDisconnectEverywhere);

    // Efface les sessions sans activité depuis cette date, et renvoie leur nombre.
    Task<int> DeleteUserSessionsInactiveSinceThisDateAsync(DateTime oldestDateOfActivityStillAccepted);
}
