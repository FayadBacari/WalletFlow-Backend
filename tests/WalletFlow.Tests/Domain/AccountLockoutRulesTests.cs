using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles du blocage d'un compte (GR Auth, SS §2, DD §3).
public class AccountLockoutRulesTests
{
    private static readonly DateTime CurrentDateUsedInTheseTests = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    // Compte ce nombre d'essais sur le compte, à la date de ces tests.
    private static void CountThisNumberOfLoginAttemptsOnTheAccount(User accountThatReceivesTheLoginAttempts, int numberOfLoginAttemptsToCount)
    {
        for (var numberOfTheAttemptBeingCounted = 1; numberOfTheAttemptBeingCounted <= numberOfLoginAttemptsToCount; numberOfTheAttemptBeingCounted++)
        {
            AccountLockoutRules.CountOneLoginAttemptAndLockAccountOnTheLastOne(accountThatReceivesTheLoginAttempts, CurrentDateUsedInTheseTests);
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
    public void LockoutDurationDoublesUpToTheMaximumOfTwentyFourHours(short numberOfLockoutsOfTheAccount, int numberOfMinutesExpectedForThisLockout)
    {
        var durationCalculatedForThisLockoutNumber = AccountLockoutRules.CalculateDurationOfTheLockoutWithThisNumber(numberOfLockoutsOfTheAccount);

        Assert.Equal(TimeSpan.FromMinutes(numberOfMinutesExpectedForThisLockout), durationCalculatedForThisLockoutNumber);
    }

    [Fact]
    public void FourLoginAttemptsDoNotLockTheAccount()
    {
        var accountBeingTestedWithTheseAttempts = new User();

        CountThisNumberOfLoginAttemptsOnTheAccount(accountBeingTestedWithTheseAttempts, 4);

        Assert.Equal(4, accountBeingTestedWithTheseAttempts.FailedLoginAttemptCount);
        Assert.Equal(0, accountBeingTestedWithTheseAttempts.LockoutCount);
        Assert.Null(accountBeingTestedWithTheseAttempts.LockoutEndsAt);
    }

    [Fact]
    public void FifthLoginAttemptLocksTheAccountForFifteenMinutes()
    {
        var accountBeingTestedWithTheseAttempts = new User();
        CountThisNumberOfLoginAttemptsOnTheAccount(accountBeingTestedWithTheseAttempts, 4);

        var thisLoginAttemptLockedTheAccount = AccountLockoutRules.CountOneLoginAttemptAndLockAccountOnTheLastOne(accountBeingTestedWithTheseAttempts, CurrentDateUsedInTheseTests);

        Assert.True(thisLoginAttemptLockedTheAccount);
        Assert.Equal(5, accountBeingTestedWithTheseAttempts.FailedLoginAttemptCount);
        Assert.Equal(1, accountBeingTestedWithTheseAttempts.LockoutCount);
        Assert.Equal(CurrentDateUsedInTheseTests.AddMinutes(15), accountBeingTestedWithTheseAttempts.LockoutEndsAt);
    }

    [Fact]
    public void AttemptsBeforeTheFifthSayTheyDidNotLockTheAccount()
    {
        var accountBeingTestedWithTheseAttempts = new User();

        var firstLoginAttemptLockedTheAccount = AccountLockoutRules.CountOneLoginAttemptAndLockAccountOnTheLastOne(accountBeingTestedWithTheseAttempts, CurrentDateUsedInTheseTests);

        Assert.False(firstLoginAttemptLockedTheAccount);
    }

    [Fact]
    public void AccountIsLockedUntilTheExactEndOfTheLockout()
    {
        var accountBeingTestedWithTheseAttempts = new User { LockoutEndsAt = CurrentDateUsedInTheseTests };

        Assert.True(AccountLockoutRules.CheckThatAccountIsLockedAtThisDate(accountBeingTestedWithTheseAttempts, CurrentDateUsedInTheseTests.AddTicks(-1)));
        Assert.False(AccountLockoutRules.CheckThatAccountIsLockedAtThisDate(accountBeingTestedWithTheseAttempts, CurrentDateUsedInTheseTests));
    }

    [Fact]
    public void AccountWithoutLockoutEndDateIsNotLocked()
    {
        Assert.False(AccountLockoutRules.CheckThatAccountIsLockedAtThisDate(new User(), CurrentDateUsedInTheseTests));
    }

    // Sans cette remise à zéro, le compteur resterait à 5 et le premier essai après le blocage bloquerait de nouveau.
    [Fact]
    public void FirstAttemptAfterTheLockoutCountsFromZeroAgain()
    {
        var accountBeingTestedWithTheseAttempts = new User
        {
            FailedLoginAttemptCount = 5,
            LockoutCount = 1,
            LockoutEndsAt = CurrentDateUsedInTheseTests
        };

        var thisLoginAttemptLockedTheAccount = AccountLockoutRules.CountOneLoginAttemptAndLockAccountOnTheLastOne(accountBeingTestedWithTheseAttempts, CurrentDateUsedInTheseTests);

        Assert.False(thisLoginAttemptLockedTheAccount);
        Assert.Equal(1, accountBeingTestedWithTheseAttempts.FailedLoginAttemptCount);
        Assert.Equal(1, accountBeingTestedWithTheseAttempts.LockoutCount);
        Assert.Null(accountBeingTestedWithTheseAttempts.LockoutEndsAt);
    }

    [Fact]
    public void SecondLockoutOfTheAccountLastsThirtyMinutes()
    {
        var accountBeingTestedWithTheseAttempts = new User();
        CountThisNumberOfLoginAttemptsOnTheAccount(accountBeingTestedWithTheseAttempts, 5);

        CountThisNumberOfLoginAttemptsOnTheAccount(accountBeingTestedWithTheseAttempts, 5);

        Assert.Equal(2, accountBeingTestedWithTheseAttempts.LockoutCount);
        Assert.Equal(CurrentDateUsedInTheseTests.AddMinutes(30), accountBeingTestedWithTheseAttempts.LockoutEndsAt);
    }

    // Le compteur s'arrête à 8 : la durée maximale est alors atteinte (DD).
    [Fact]
    public void LockoutCountStopsAtEightAndTheDurationStaysAtTwentyFourHours()
    {
        var accountBeingTestedWithTheseAttempts = new User { LockoutCount = 8 };

        CountThisNumberOfLoginAttemptsOnTheAccount(accountBeingTestedWithTheseAttempts, 5);

        Assert.Equal(8, accountBeingTestedWithTheseAttempts.LockoutCount);
        Assert.Equal(CurrentDateUsedInTheseTests.AddHours(24), accountBeingTestedWithTheseAttempts.LockoutEndsAt);
    }

    // Un compteur à 5 sans date de fin ne devrait pas exister. S'il existe, le compte ne doit pas rester coincé,
    // ni dépasser la limite de la base (nb_tentatives_echouees de 0 à 5).
    [Fact]
    public void CounterAtTheMaximumWithoutEndDateGivesFiveNewAttempts()
    {
        var accountBeingTestedWithTheseAttempts = new User { FailedLoginAttemptCount = 5 };

        AccountLockoutRules.CountOneLoginAttemptAndLockAccountOnTheLastOne(accountBeingTestedWithTheseAttempts, CurrentDateUsedInTheseTests);

        Assert.Equal(1, accountBeingTestedWithTheseAttempts.FailedLoginAttemptCount);
    }

    // Le lien de déblocage et la récupération n'effacent pas l'historique : le prochain blocage reste plus long (GR Auth).
    [Fact]
    public void UnlockingEndsTheLockoutButKeepsTheNumberOfLockouts()
    {
        var accountBeingTestedWithTheseAttempts = new User
        {
            FailedLoginAttemptCount = 5,
            LockoutCount = 3,
            LockoutEndsAt = CurrentDateUsedInTheseTests.AddHours(1)
        };

        AccountLockoutRules.RemoveCurrentLockoutOfThisAccount(accountBeingTestedWithTheseAttempts);

        Assert.Equal(0, accountBeingTestedWithTheseAttempts.FailedLoginAttemptCount);
        Assert.Null(accountBeingTestedWithTheseAttempts.LockoutEndsAt);
        Assert.Equal(3, accountBeingTestedWithTheseAttempts.LockoutCount);
    }

    [Fact]
    public void SuccessfulLoginResetsEveryCounterOfTheAccount()
    {
        var accountBeingTestedWithTheseAttempts = new User
        {
            FailedLoginAttemptCount = 5,
            LockoutCount = 3,
            LockoutEndsAt = CurrentDateUsedInTheseTests
        };

        AccountLockoutRules.ResetLoginCountersAfterSuccessfulLogin(accountBeingTestedWithTheseAttempts);

        Assert.Equal(0, accountBeingTestedWithTheseAttempts.FailedLoginAttemptCount);
        Assert.Equal(0, accountBeingTestedWithTheseAttempts.LockoutCount);
        Assert.Null(accountBeingTestedWithTheseAttempts.LockoutEndsAt);
    }
}
