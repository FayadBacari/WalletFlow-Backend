namespace WalletFlow.Application.UserSessions;

// Ce qui s'est passé quand la connexion a compté un essai.
public enum LoginAttemptCountResult
{
    Refused,
    Counted,
    CountedAndLocked
}
