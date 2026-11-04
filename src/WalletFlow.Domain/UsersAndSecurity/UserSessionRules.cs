namespace WalletFlow.Domain.UsersAndSecurity;

// Règle de fermeture d'une session (GR Auth, SS §3, DD §4.4).
public static class UserSessionRules
{
    public static readonly TimeSpan DurationOfInactivityThatClosesTheSession = TimeSpan.FromMinutes(30);

    // Renvoie vrai si la session a servi il y a moins de 30 minutes.
    public static bool CheckThatSessionIsStillActiveAtThisDate(UserSession sessionToCheckForItsInactivity, DateTime dateOfTheCurrentHttpRequest)
    {
        return sessionToCheckForItsInactivity.LastActivityAt + DurationOfInactivityThatClosesTheSession > dateOfTheCurrentHttpRequest;
    }
}
