namespace WalletFlow.Domain.UsersAndSecurity;

// Nombres et durées du blocage d'un compte après des échecs de connexion (GR Auth, SS §2, DD §3).
// Les règles elles-mêmes sont dans le compte : voir User.
public static class AccountLockoutRules
{
    public const short MaximumFailedAttempts = 5;
    public const short MaximumLockoutCount = 8;

    public static readonly TimeSpan FirstLockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaximumLockoutDuration = TimeSpan.FromHours(24);

    // Donne la durée d'un blocage : 15 minutes × 2^(nb_blocages − 1), 24 heures au plus (DD).
    public static TimeSpan CalculateLockoutDuration(short lockoutCount)
    {
        var duration = FirstLockoutDuration;

        for (var lockoutNumber = 2; lockoutNumber <= lockoutCount; lockoutNumber++)
        {
            duration = duration * 2;
        }

        if (duration > MaximumLockoutDuration)
        {
            return MaximumLockoutDuration;
        }

        return duration;
    }
}
