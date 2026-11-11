using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserSessions;

// Cas d'utilisation : reconnaître une session à chaque requête, et la fermer à la déconnexion (GR Auth, SS §3).
public class UserSessionService
{
    private readonly IUserSessionRepository sessionRepository;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly TimeProvider clock;

    // Reçoit les outils dont les sessions ont besoin.
    public UserSessionService(
        IUserSessionRepository sessionRepository,
        ISecureTokenGenerator tokenGenerator,
        TimeProvider clock)
    {
        this.sessionRepository = sessionRepository;
        this.tokenGenerator = tokenGenerator;
        this.clock = clock;
    }

    // Donne l'identifiant du propriétaire si la session est encore active, et note qu'elle vient de servir,
    // au plus une fois par minute (voir UserSessionRules). Renvoie null si le cookie ne correspond à aucune session, ou si la session a expiré.
    public async Task<Guid?> FindOwnerAndRecordActivityAsync(string sessionToken)
    {
        var sessionTokenHash = tokenGenerator.HashToken(sessionToken);
        var now = clock.GetUtcNow().UtcDateTime;

        var session = await sessionRepository.FindAsync(sessionTokenHash);
        if (session == null || !session.IsActiveAt(now))
        {
            return null;
        }

        if (session.ShouldRecordActivityAt(now))
        {
            await sessionRepository.RecordActivityAsync(sessionTokenHash, now);
        }
        return session.UserId;
    }

    // Détruit la session côté serveur, pas seulement le cookie (SS §3).
    public async Task CloseAsync(string sessionToken)
    {
        var sessionTokenHash = tokenGenerator.HashToken(sessionToken);

        await sessionRepository.DeleteAsync(sessionTokenHash);
    }
}
