using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserSessions;

// Accès à la base pour les sessions et les appareils. Implémenté dans la couche Infrastructure.
// Les méthodes « Prepare… » n'écrivent rien : voir IDatabaseUnitOfWork. Les autres agissent tout de suite.
public interface IUserSessionRepository
{
    // Prépare l'enregistrement d'une nouvelle session.
    void PrepareSession(UserSession session);

    // Prépare l'effacement de toutes les sessions du compte, après un changement de mot de passe (SS §3).
    Task PrepareAllSessionsRemovalAsync(Guid userId, CancellationToken cancellationToken = default);

    // Prépare l'enregistrement d'un nouvel appareil du compte.
    void PrepareDevice(Device device);

    // Renvoie vrai si cette empreinte de jeton d'appareil appartient à ce compte.
    Task<bool> DeviceBelongsToAccountAsync(string deviceTokenHash, Guid accountId, CancellationToken cancellationToken = default);

    // Renvoie vrai si le compte a déjà au moins un appareil : sinon, c'est sa toute première connexion.
    Task<bool> HasDeviceAsync(Guid accountId, CancellationToken cancellationToken = default);

    // Cherche une session par l'empreinte de son identifiant. Renvoie null si elle n'existe pas.
    // La session lue n'est pas suivie : sa date d'activité s'écrit avec RecordActivityAsync.
    Task<UserSession?> FindAsync(string sessionTokenHash, CancellationToken cancellationToken = default);

    // Note tout de suite que la session vient de servir.
    Task RecordActivityAsync(string sessionTokenHash, DateTime now, CancellationToken cancellationToken = default);

    // Détruit tout de suite une session. Ne fait rien si elle n'existe plus.
    Task DeleteAsync(string sessionTokenHash, CancellationToken cancellationToken = default);

    // Détruit tout de suite toutes les sessions d'un compte, après une récupération de mot de passe (SS §3).
    Task DeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    // Efface les sessions sans activité depuis cette date, ou ouvertes avant cette autre date, et renvoie leur nombre.
    Task<int> DeleteExpiredAsync(DateTime activityCutoff, DateTime creationCutoff, CancellationToken cancellationToken = default);

    // Prépare la fermeture des sessions les moins récemment actives du compte, pour n'en garder que ce nombre.
    Task PrepareOldestSessionsRemovalAsync(Guid userId, int keptCount, CancellationToken cancellationToken = default);

    // Prépare l'oubli des appareils les moins récemment utilisés du compte, pour n'en garder que ce nombre.
    Task PrepareOldestDevicesRemovalAsync(Guid userId, int keptCount, CancellationToken cancellationToken = default);

    // Prépare la note d'une connexion depuis cet appareil du compte.
    Task PrepareDeviceUseAsync(string deviceTokenHash, Guid accountId, DateTime now, CancellationToken cancellationToken = default);

    // Efface les appareils inutilisés depuis cette date, sauf le plus récemment utilisé de chaque compte, et renvoie leur nombre.
    Task<int> DeleteForgottenDevicesAsync(DateTime usedBefore, CancellationToken cancellationToken = default);
}
