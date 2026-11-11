using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserSessions;

// Cas d'utilisation : efface les sessions expirées. Elles sont déjà refusées à chaque requête :
// l'effacement évite seulement que la table ne grossisse quand on ferme son navigateur sans se déconnecter.
public class ExpiredUserSessionsCleanupService
{
    private readonly IUserSessionRepository sessionRepository;
    private readonly TimeProvider clock;

    // Reçoit l'accès à la base et l'horloge.
    public ExpiredUserSessionsCleanupService(
        IUserSessionRepository sessionRepository,
        TimeProvider clock)
    {
        this.sessionRepository = sessionRepository;
        this.clock = clock;
    }

    // Efface les sessions concernées, et renvoie leur nombre.
    public async Task<int> DeleteExpiredSessionsAsync()
    {
        var activityCutoff = clock.GetUtcNow().UtcDateTime - UserSessionRules.IdleTimeout;

        return await sessionRepository.DeleteInactiveSinceAsync(activityCutoff);
    }
}
