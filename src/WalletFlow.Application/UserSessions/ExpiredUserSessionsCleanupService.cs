using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserSessions;

// Cas d'utilisation : efface les sessions expirées. Elles sont déjà refusées à chaque requête :
// l'effacement évite seulement que la table ne grossisse quand on ferme son navigateur sans se déconnecter.
public class ExpiredUserSessionsCleanupService
{
    private readonly IUserSessionRepository repositoryOfTheUserSessionsInDatabase;
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;

    // Reçoit l'accès à la base et l'horloge.
    public ExpiredUserSessionsCleanupService(
        IUserSessionRepository repositoryOfTheUserSessionsInDatabase,
        TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.repositoryOfTheUserSessionsInDatabase = repositoryOfTheUserSessionsInDatabase;
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Efface les sessions concernées, et renvoie leur nombre.
    public async Task<int> DeleteExpiredUserSessionsFromTheDatabaseAsync()
    {
        var oldestDateOfActivityStillAccepted = clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime - UserSessionRules.DurationOfInactivityThatClosesTheSession;

        return await repositoryOfTheUserSessionsInDatabase.DeleteUserSessionsInactiveSinceThisDateAsync(oldestDateOfActivityStillAccepted);
    }
}
