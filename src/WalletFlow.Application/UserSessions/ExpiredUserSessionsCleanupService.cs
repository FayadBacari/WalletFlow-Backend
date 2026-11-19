using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserSessions;

// Cas d'utilisation : efface les sessions expirées et les appareils oubliés. Les sessions sont déjà refusées à chaque requête :
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

    // Efface les sessions expirées, inactives depuis 30 minutes ou ouvertes depuis 12 heures, et renvoie leur nombre.
    public async Task<int> DeleteExpiredSessionsAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        return await sessionRepository.DeleteExpiredAsync(now - UserSessionRules.IdleTimeout, now - UserSessionRules.AbsoluteLifetime, cancellationToken);
    }

    // Oublie les appareils inutilisés depuis un an, sauf le plus récent de chaque compte, et renvoie leur nombre (SS §3).
    public async Task<int> DeleteForgottenDevicesAsync(CancellationToken cancellationToken = default)
    {
        return await sessionRepository.DeleteForgottenDevicesAsync(clock.GetUtcNow().UtcDateTime - DeviceRules.Lifetime, cancellationToken);
    }
}
