using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Lit et écrit les sessions et les appareils dans la base PostgreSQL.
// Les méthodes « Prepare… » n'écrivent rien : l'unité de travail enregistre tout ensemble (voir IDatabaseUnitOfWork).
public class UserSessionRepository : IUserSessionRepository
{
    private readonly WalletFlowDatabaseContext databaseContext;

    // Reçoit l'accès à la base de la requête en cours.
    public UserSessionRepository(WalletFlowDatabaseContext databaseContext)
    {
        this.databaseContext = databaseContext;
    }

    // Prépare l'enregistrement d'une nouvelle session.
    public void PrepareSession(UserSession session)
    {
        databaseContext.UserSessions.Add(session);
    }

    // Prépare l'effacement de toutes les sessions du compte, avec les autres changements (SS §3).
    // Une session effacée entre-temps, par exemple par une déconnexion, fait échouer l'enregistrement, qui recommence.
    public async Task PrepareAllSessionsRemovalAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var sessions = await databaseContext.UserSessions
            .Where(session => session.UserId == userId)
            .ToListAsync(cancellationToken);

        databaseContext.UserSessions.RemoveRange(sessions);
    }

    // Prépare l'enregistrement d'un nouvel appareil du compte.
    public void PrepareDevice(Device device)
    {
        databaseContext.Devices.Add(device);
    }

    // Renvoie vrai si cette empreinte de jeton d'appareil appartient à ce compte.
    public async Task<bool> DeviceBelongsToAccountAsync(string deviceTokenHash, Guid accountId, CancellationToken cancellationToken = default)
    {
        return await databaseContext.Devices.AnyAsync(device =>
            device.DeviceToken == deviceTokenHash
            && device.UserId == accountId,
            cancellationToken);
    }

    // Renvoie vrai si le compte a déjà au moins un appareil : sinon, c'est sa toute première connexion.
    public async Task<bool> HasDeviceAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return await databaseContext.Devices.AnyAsync(device => device.UserId == accountId, cancellationToken);
    }

    // Cherche une session par l'empreinte de son identifiant. Renvoie null si elle n'existe pas.
    public async Task<UserSession?> FindAsync(string sessionTokenHash, CancellationToken cancellationToken = default)
    {
        return await databaseContext.UserSessions.AsNoTracking().SingleOrDefaultAsync(session =>
            session.Id == sessionTokenHash,
            cancellationToken);
    }

    // Note tout de suite que la session vient de servir. Comme dans UserSession, la date ne recule jamais :
    // deux requêtes qui écrivent dans le désordre ne raccourcissent pas la session.
    public async Task RecordActivityAsync(string sessionTokenHash, DateTime now, CancellationToken cancellationToken = default)
    {
        await databaseContext.UserSessions
            .Where(session =>
                session.Id == sessionTokenHash
                && session.LastActivityAt < now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                session => session.LastActivityAt,
                now),
                cancellationToken);
    }

    // Détruit une session. Ne fait rien si elle n'existe plus.
    public async Task DeleteAsync(string sessionTokenHash, CancellationToken cancellationToken = default)
    {
        await databaseContext.UserSessions
            .Where(session => session.Id == sessionTokenHash)
            .ExecuteDeleteAsync(cancellationToken);
    }

    // Détruit toutes les sessions d'un compte, après une récupération de mot de passe (SS §3).
    public async Task DeleteAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await databaseContext.UserSessions
            .Where(session => session.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    // Efface les sessions sans activité depuis cette date, ou ouvertes avant cette autre date, et renvoie leur nombre.
    public async Task<int> DeleteExpiredAsync(DateTime activityCutoff, DateTime creationCutoff, CancellationToken cancellationToken = default)
    {
        return await databaseContext.UserSessions
            .Where(session => session.LastActivityAt <= activityCutoff || session.CreatedAt <= creationCutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    // Prépare la fermeture des sessions les moins récemment actives du compte, avec les autres changements. EF Core les suit :
    // une session déjà effacée par une autre requête fait échouer l'enregistrement, qui recommence.
    public async Task PrepareOldestSessionsRemovalAsync(Guid userId, int keptCount, CancellationToken cancellationToken = default)
    {
        var oldestSessions = await databaseContext.UserSessions
            .Where(session => session.UserId == userId)
            .OrderByDescending(session => session.LastActivityAt)
            .ThenBy(session => session.Id)
            .Skip(keptCount)
            .ToListAsync(cancellationToken);

        databaseContext.UserSessions.RemoveRange(oldestSessions);
    }

    // Prépare l'oubli des appareils les moins récemment utilisés du compte, avec les autres changements.
    public async Task PrepareOldestDevicesRemovalAsync(Guid userId, int keptCount, CancellationToken cancellationToken = default)
    {
        var oldestDevices = await databaseContext.Devices
            .Where(device => device.UserId == userId)
            .OrderByDescending(device => device.LastUsedAt)
            .ThenBy(device => device.Id)
            .Skip(keptCount)
            .ToListAsync(cancellationToken);

        databaseContext.Devices.RemoveRange(oldestDevices);
    }

    // Prépare la note d'une connexion depuis cet appareil du compte : EF Core suit l'appareil lu, et l'écrit avec la connexion.
    public async Task PrepareDeviceUseAsync(string deviceTokenHash, Guid accountId, DateTime now, CancellationToken cancellationToken = default)
    {
        var device = await databaseContext.Devices.SingleOrDefaultAsync(device =>
            device.DeviceToken == deviceTokenHash
            && device.UserId == accountId,
            cancellationToken);

        device?.RecordUse(now);
    }

    // Efface les appareils inutilisés depuis cette date. Le plus récemment utilisé de chaque compte reste : sans lui, la connexion
    // suivante passerait pour la toute première du compte, et l'alerte « nouvel appareil » ne partirait pas.
    public async Task<int> DeleteForgottenDevicesAsync(DateTime usedBefore, CancellationToken cancellationToken = default)
    {
        return await databaseContext.Devices
            .Where(device =>
                device.LastUsedAt <= usedBefore
                && databaseContext.Devices.Any(other => other.UserId == device.UserId && other.LastUsedAt > device.LastUsedAt))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
