using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la limite de 3 demandes par heure et par adresse e-mail (SS §6).
public class InMemoryEmailAddressRequestLimiterTests
{
    private const string EmailAddressCountedInTheseTests = "visiteur@exemple.test";

    private readonly FakeTimeProviderForTests clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();

    // Crée la limite à tester, avec la fausse horloge.
    private InMemoryEmailAddressRequestLimiter CreateLimiterWithTheFakeClock()
    {
        return new InMemoryEmailAddressRequestLimiter(clockThatOnlyMovesWhenTestsAskIt);
    }

    [Fact]
    public void ThreeRequestsAreAcceptedAndTheFourthIsRefused()
    {
        var limiterBeingTestedWithTheFakeClock = CreateLimiterWithTheFakeClock();

        Assert.True(limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress(EmailAddressCountedInTheseTests));
        Assert.True(limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress(EmailAddressCountedInTheseTests));
        Assert.True(limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress(EmailAddressCountedInTheseTests));
        Assert.False(limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress(EmailAddressCountedInTheseTests));
    }

    [Fact]
    public void LimitOfOneAddressDoesNotBlockAnotherAddress()
    {
        var limiterBeingTestedWithTheFakeClock = CreateLimiterWithTheFakeClock();
        for (var numberOfTheRequestBeingCounted = 1; numberOfTheRequestBeingCounted <= 4; numberOfTheRequestBeingCounted++)
        {
            limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress(EmailAddressCountedInTheseTests);
        }

        Assert.True(limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress("autre-visiteur@exemple.test"));
    }

    [Fact]
    public void LimitStaysActiveUntilOneSecondBeforeTheEndOfTheHour()
    {
        var limiterBeingTestedWithTheFakeClock = CreateLimiterWithTheFakeClock();
        for (var numberOfTheRequestBeingCounted = 1; numberOfTheRequestBeingCounted <= 3; numberOfTheRequestBeingCounted++)
        {
            limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress(EmailAddressCountedInTheseTests);
        }

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromHours(1) - TimeSpan.FromSeconds(1));

        Assert.False(limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress(EmailAddressCountedInTheseTests));
    }

    [Fact]
    public void AddressIsAcceptedAgainAfterOneHour()
    {
        var limiterBeingTestedWithTheFakeClock = CreateLimiterWithTheFakeClock();
        for (var numberOfTheRequestBeingCounted = 1; numberOfTheRequestBeingCounted <= 4; numberOfTheRequestBeingCounted++)
        {
            limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress(EmailAddressCountedInTheseTests);
        }

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromHours(1));

        Assert.True(limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress(EmailAddressCountedInTheseTests));
    }

    // Des demandes refusées ne prolongent pas le blocage : la période part de la première demande.
    [Fact]
    public void RefusedRequestsDoNotExtendTheCountingWindow()
    {
        var limiterBeingTestedWithTheFakeClock = CreateLimiterWithTheFakeClock();
        for (var numberOfTheRequestBeingCounted = 1; numberOfTheRequestBeingCounted <= 3; numberOfTheRequestBeingCounted++)
        {
            limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress(EmailAddressCountedInTheseTests);
        }

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(59));
        limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress(EmailAddressCountedInTheseTests);
        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(1));

        Assert.True(limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress(EmailAddressCountedInTheseTests));
    }

    // Plusieurs requêtes arrivent en même temps : la limite doit rester exacte.
    [Fact]
    public async Task SimultaneousRequestsNeverExceedTheLimit()
    {
        var limiterBeingTestedWithTheFakeClock = CreateLimiterWithTheFakeClock();
        var simultaneousRequestsBeingCountedByTheLimiter = new List<Task<bool>>();

        for (var numberOfTheRequestBeingCounted = 1; numberOfTheRequestBeingCounted <= 200; numberOfTheRequestBeingCounted++)
        {
            simultaneousRequestsBeingCountedByTheLimiter.Add(Task.Run(() =>
                limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress(EmailAddressCountedInTheseTests)));
        }

        var answersGivenToTheSimultaneousRequests = await Task.WhenAll(simultaneousRequestsBeingCountedByTheLimiter);

        var numberOfRequestsAcceptedByTheLimiter = 0;
        foreach (var answerGivenToOneSimultaneousRequest in answersGivenToTheSimultaneousRequests)
        {
            if (answerGivenToOneSimultaneousRequest)
            {
                numberOfRequestsAcceptedByTheLimiter = numberOfRequestsAcceptedByTheLimiter + 1;
            }
        }

        Assert.Equal(3, numberOfRequestsAcceptedByTheLimiter);
    }

    // Beaucoup de requêtes simultanées sur des adresses différentes : sans verrou, les compteurs se corrompraient.
    [Fact]
    public async Task SimultaneousRequestsOnManyAddressesKeepEveryCounterExact()
    {
        var limiterBeingTestedWithTheFakeClock = CreateLimiterWithTheFakeClock();
        var tasksSendingRequestsAtTheSameTime = new List<Task>();

        for (var numberOfTheTaskBeingStarted = 1; numberOfTheTaskBeingStarted <= 8; numberOfTheTaskBeingStarted++)
        {
            var prefixOfTheAddressesOfThisTask = "visiteur-" + numberOfTheTaskBeingStarted + "-";

            tasksSendingRequestsAtTheSameTime.Add(Task.Run(() =>
            {
                for (var numberOfTheAddressBeingCounted = 1; numberOfTheAddressBeingCounted <= 500; numberOfTheAddressBeingCounted++)
                {
                    limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress(prefixOfTheAddressesOfThisTask + numberOfTheAddressBeingCounted + "@exemple.test");
                }
            }));
        }

        await Task.WhenAll(tasksSendingRequestsAtTheSameTime);

        // Chaque adresse a été comptée une fois : il lui reste exactement 2 demandes.
        Assert.True(limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress("visiteur-1-1@exemple.test"));
        Assert.True(limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress("visiteur-1-1@exemple.test"));
        Assert.False(limiterBeingTestedWithTheFakeClock.TryToCountNewRequestForThisEmailAddress("visiteur-1-1@exemple.test"));
    }
}
