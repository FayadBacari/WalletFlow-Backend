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
    public async Task PrepareAllSessionsRemovalAsync(Guid userId)
    {
        var sessions = await databaseContext.UserSessions
            .Where(session => session.UserId == userId)
            .ToListAsync();

        databaseContext.UserSessions.RemoveRange(sessions);
    }

    // Prépare l'enregistrement d'un nouvel appareil du compte.
    public void PrepareDevice(Device device)
    {
        databaseContext.Devices.Add(device);
    }

    // Renvoie vrai si cette empreinte de jeton d'appareil appartient à ce compte.
    public async Task<bool> DeviceBelongsToAccountAsync(string deviceTokenHash, Guid accountId)
    {
        return await databaseContext.Devices.AnyAsync(device =>
            device.DeviceToken == deviceTokenHash
            && device.UserId == accountId);
    }

    // Cherche une session par l'empreinte de son identifiant. Renvoie null si elle n'existe pas.
    public async Task<UserSession?> FindAsync(string sessionTokenHash)
    {
        return await databaseContext.UserSessions.AsNoTracking().SingleOrDefaultAsync(session =>
            session.Id == sessionTokenHash);
    }

    // Note tout de suite que la session vient de servir. Comme dans UserSession, la date ne recule jamais :
    // deux requêtes qui écrivent dans le désordre ne raccourcissent pas la session.
    public async Task RecordActivityAsync(string sessionTokenHash, DateTime now)
    {
        await databaseContext.UserSessions
            .Where(session =>
                session.Id == sessionTokenHash
                && session.LastActivityAt < now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                session => session.LastActivityAt,
                now));
    }

    // Détruit une session. Ne fait rien si elle n'existe plus.
    public async Task DeleteAsync(string sessionTokenHash)
    {
        await databaseContext.UserSessions
            .Where(session => session.Id == sessionTokenHash)
            .ExecuteDeleteAsync();
    }

    // Détruit toutes les sessions d'un compte, après une récupération de mot de passe (SS §3).
    public async Task DeleteAllForUserAsync(Guid userId)
    {
        await databaseContext.UserSessions
            .Where(session => session.UserId == userId)
            .ExecuteDeleteAsync();
    }

    // Efface les sessions sans activité depuis cette date, et renvoie leur nombre.
    public async Task<int> DeleteInactiveSinceAsync(DateTime activityCutoff)
    {
        return await databaseContext.UserSessions
            .Where(session => session.LastActivityAt <= activityCutoff)
            .ExecuteDeleteAsync();
    }
}
