using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles du blocage d'un compte (GR Auth, SS §2, DD §3).
public class AccountLockoutRulesTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    // Compte ce nombre d'essais sur le compte, à la date de ces tests.
    private static void CountLoginAttempts(User account, int attemptCount)
    {
        for (var attemptNumber = 1; attemptNumber <= attemptCount; attemptNumber++)
        {
            account.CountLoginAttempt(Now);
        }
    }

    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 30)]
    [InlineData(3, 60)]
    [InlineData(4, 120)]
    [InlineData(5, 240)]
    [InlineData(6, 480)]
    [InlineData(7, 960)]
    [InlineData(8, 1440)] // 1920 minutes sans la limite de 24 heures
    public void LockoutDurationDoublesUpToTheMaximumOfTwentyFourHours(short lockoutCount, int expectedMinutes)
    {
        var duration = AccountLockoutRules.CalculateLockoutDuration(lockoutCount);

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), duration);
    }

    [Fact]
    public void FourLoginAttemptsDoNotLockTheAccount()
    {
        var account = new User();

        CountLoginAttempts(account, 4);

        Assert.Equal(4, account.FailedLoginAttemptCount);
        Assert.Equal(0, account.LockoutCount);
        Assert.Null(account.LockoutEndsAt);
    }

    [Fact]
    public void FifthLoginAttemptLocksTheAccountForFifteenMinutes()
    {
        var account = new User();
        CountLoginAttempts(account, 4);

        var lockedNow = account.CountLoginAttempt(Now);

        Assert.True(lockedNow);
        Assert.Equal(5, account.FailedLoginAttemptCount);
        Assert.Equal(1, account.LockoutCount);
        Assert.Equal(Now.AddMinutes(15), account.LockoutEndsAt);
    }

    [Fact]
    public void AttemptsBeforeTheFifthSayTheyDidNotLockTheAccount()
    {
        var account = new User();

        var firstAttemptLocked = account.CountLoginAttempt(Now);

        Assert.False(firstAttemptLocked);
    }

    [Fact]
    public void AccountIsLockedUntilTheExactEndOfTheLockout()
    {
        var account = new User { LockoutEndsAt = Now };

        Assert.True(account.IsLockedAt(Now.AddTicks(-1)));
        Assert.False(account.IsLockedAt(Now));
    }

    [Fact]
    public void AccountWithoutLockoutEndDateIsNotLocked()
    {
        Assert.False(new User().IsLockedAt(Now));
    }

    // Sans cette remise à zéro, le compteur resterait à 5 et le premier essai après le blocage bloquerait de nouveau.
    [Fact]
    public void FirstAttemptAfterTheLockoutCountsFromZeroAgain()
    {
        var account = new User
        {
            FailedLoginAttemptCount = 5,
            LockoutCount = 1,
            LockoutEndsAt = Now
        };

        var lockedNow = account.CountLoginAttempt(Now);

        Assert.False(lockedNow);
        Assert.Equal(1, account.FailedLoginAttemptCount);
        Assert.Equal(1, account.LockoutCount);
        Assert.Null(account.LockoutEndsAt);
    }

    [Fact]
    public void SecondLockoutOfTheAccountLastsThirtyMinutes()
    {
        var account = new User();
        CountLoginAttempts(account, 5);

        CountLoginAttempts(account, 5);

        Assert.Equal(2, account.LockoutCount);
        Assert.Equal(Now.AddMinutes(30), account.LockoutEndsAt);
    }

    // Le compteur s'arrête à 8 : la durée maximale est alors atteinte (DD).
    [Fact]
    public void LockoutCountStopsAtEightAndTheDurationStaysAtTwentyFourHours()
    {
        var account = new User { LockoutCount = 8 };

        CountLoginAttempts(account, 5);

        Assert.Equal(8, account.LockoutCount);
        Assert.Equal(Now.AddHours(24), account.LockoutEndsAt);
    }

    // Un compteur à 5 sans date de fin ne devrait pas exister. S'il existe, le compte ne doit pas rester coincé,
    // ni dépasser la limite de la base (nb_tentatives_echouees de 0 à 5).
    [Fact]
    public void CounterAtTheMaximumWithoutEndDateGivesFiveNewAttempts()
    {
        var account = new User { FailedLoginAttemptCount = 5 };

        account.CountLoginAttempt(Now);

        Assert.Equal(1, account.FailedLoginAttemptCount);
    }

    // Le lien de déblocage et la récupération n'effacent pas l'historique : le prochain blocage reste plus long (GR Auth).
    [Fact]
    public void UnlockingEndsTheLockoutButKeepsTheNumberOfLockouts()
    {
        var account = new User
        {
            FailedLoginAttemptCount = 5,
            LockoutCount = 3,
            LockoutEndsAt = Now.AddHours(1)
        };

        account.Unlock();

        Assert.Equal(0, account.FailedLoginAttemptCount);
        Assert.Null(account.LockoutEndsAt);
        Assert.Equal(3, account.LockoutCount);
    }

    [Fact]
    public void SuccessfulLoginResetsEveryCounterOfTheAccount()
    {
        var account = new User
        {
            FailedLoginAttemptCount = 5,
            LockoutCount = 3,
            LockoutEndsAt = Now
        };

        account.ResetLoginCounters();

        Assert.Equal(0, account.FailedLoginAttemptCount);
        Assert.Equal(0, account.LockoutCount);
        Assert.Null(account.LockoutEndsAt);
    }
}
