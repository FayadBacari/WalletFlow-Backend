using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserSessions;

// Accès à la base pour les sessions et les appareils. Implémenté dans la couche Infrastructure.
// Les méthodes « Prepare… » n'écrivent rien : voir IDatabaseUnitOfWork. Les autres agissent tout de suite.
public interface IUserSessionRepository
{
    // Prépare l'enregistrement d'une nouvelle session.
    void PrepareSession(UserSession session);

    // Prépare l'enregistrement d'un nouvel appareil du compte.
    void PrepareDevice(Device device);

    // Renvoie vrai si cette empreinte de jeton d'appareil appartient à ce compte.
    Task<bool> DeviceBelongsToAccountAsync(string deviceTokenHash, Guid accountId);

    // Cherche une session par l'empreinte de son identifiant. Renvoie null si elle n'existe pas.
    // La session lue n'est pas suivie : sa date d'activité s'écrit avec RecordActivityAsync.
    Task<UserSession?> FindAsync(string sessionTokenHash);

    // Note tout de suite que la session vient de servir.
    Task RecordActivityAsync(string sessionTokenHash, DateTime now);

    // Détruit tout de suite une session. Ne fait rien si elle n'existe plus.
    Task DeleteAsync(string sessionTokenHash);

    // Détruit tout de suite toutes les sessions d'un compte, après une récupération de mot de passe (SS §3).
    Task DeleteAllForUserAsync(Guid userId);

    // Efface tout de suite les sessions sans activité depuis cette date, et renvoie leur nombre.
    Task<int> DeleteInactiveSinceAsync(DateTime activityCutoff);
}
