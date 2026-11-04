using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserSessions;

// Cas d'utilisation : reconnaître une session à chaque requête, et la fermer à la déconnexion (GR Auth, SS §3).
public class UserSessionService
{
    private readonly IUserSessionRepository repositoryOfTheUserSessionsInDatabase;
    private readonly ISecureTokenGenerator generatorOfTheSecureRandomTokens;
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;

    // Reçoit les outils dont les sessions ont besoin.
    public UserSessionService(
        IUserSessionRepository repositoryOfTheUserSessionsInDatabase,
        ISecureTokenGenerator generatorOfTheSecureRandomTokens,
        TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.repositoryOfTheUserSessionsInDatabase = repositoryOfTheUserSessionsInDatabase;
        this.generatorOfTheSecureRandomTokens = generatorOfTheSecureRandomTokens;
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Donne l'identifiant du propriétaire si la session est encore active, et note qu'elle vient de servir.
    // Renvoie null si le cookie ne correspond à aucune session, ou si la session a expiré.
    public async Task<Guid?> FindOwnerOfTheActiveSessionThenRecordItsActivityAsync(string sessionTokenReadInTheCookie)
    {
        var hashOfTheTokenOfTheSession = generatorOfTheSecureRandomTokens.ConvertSecureTokenIntoItsHash(sessionTokenReadInTheCookie);
        var currentDateOfTheIncomingRequest = clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime;

        var sessionFoundWithItsHashInDatabase = await repositoryOfTheUserSessionsInDatabase.FindUserSessionByItsHashAsync(hashOfTheTokenOfTheSession);
        if (sessionFoundWithItsHashInDatabase == null || !UserSessionRules.CheckThatSessionIsStillActiveAtThisDate(sessionFoundWithItsHashInDatabase, currentDateOfTheIncomingRequest))
        {
            return null;
        }

        await repositoryOfTheUserSessionsInDatabase.RecordActivityOfThisUserSessionAsync(hashOfTheTokenOfTheSession, currentDateOfTheIncomingRequest);
        return sessionFoundWithItsHashInDatabase.UserId;
    }

    // Détruit la session côté serveur, pas seulement le cookie (SS §3).
    public async Task CloseUserSessionOfThisCookieAsync(string sessionTokenReadInTheCookie)
    {
        var hashOfTheTokenOfTheSession = generatorOfTheSecureRandomTokens.ConvertSecureTokenIntoItsHash(sessionTokenReadInTheCookie);

        await repositoryOfTheUserSessionsInDatabase.DeleteUserSessionByItsHashAsync(hashOfTheTokenOfTheSession);
    }
}
