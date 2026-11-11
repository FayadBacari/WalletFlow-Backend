using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la limite de 3 demandes par heure et par adresse e-mail (SS §6).
public class InMemoryEmailAddressRequestLimiterTests
{
    private const string EmailAddress = "visiteur@exemple.test";

    private readonly FakeTimeProvider clock = new FakeTimeProvider();

    // Crée la limite à tester, avec la fausse horloge.
    private InMemoryEmailAddressRequestLimiter CreateLimiter()
    {
        return new InMemoryEmailAddressRequestLimiter(clock);
    }

    [Fact]
    public void ThreeRequestsAreAcceptedAndTheFourthIsRefused()
    {
        var limiter = CreateLimiter();

        Assert.True(limiter.TryCountRequest(EmailAddress));
        Assert.True(limiter.TryCountRequest(EmailAddress));
        Assert.True(limiter.TryCountRequest(EmailAddress));
        Assert.False(limiter.TryCountRequest(EmailAddress));
    }

    [Fact]
    public void LimitOfOneAddressDoesNotBlockAnotherAddress()
    {
        var limiter = CreateLimiter();
        for (var requestNumber = 1; requestNumber <= 4; requestNumber++)
        {
            limiter.TryCountRequest(EmailAddress);
        }

        Assert.True(limiter.TryCountRequest("autre-visiteur@exemple.test"));
    }

    [Fact]
    public void LimitStaysActiveUntilOneSecondBeforeTheEndOfTheHour()
    {
        var limiter = CreateLimiter();
        for (var requestNumber = 1; requestNumber <= 3; requestNumber++)
        {
            limiter.TryCountRequest(EmailAddress);
        }

        clock.Advance(TimeSpan.FromHours(1) - TimeSpan.FromSeconds(1));

        Assert.False(limiter.TryCountRequest(EmailAddress));
    }

    [Fact]
    public void AddressIsAcceptedAgainAfterOneHour()
    {
        var limiter = CreateLimiter();
        for (var requestNumber = 1; requestNumber <= 4; requestNumber++)
        {
            limiter.TryCountRequest(EmailAddress);
        }

        clock.Advance(TimeSpan.FromHours(1));

        Assert.True(limiter.TryCountRequest(EmailAddress));
    }

    // Des demandes refusées ne prolongent pas le blocage : la période part de la première demande.
    [Fact]
    public void RefusedRequestsDoNotExtendTheCountingWindow()
    {
        var limiter = CreateLimiter();
        for (var requestNumber = 1; requestNumber <= 3; requestNumber++)
        {
            limiter.TryCountRequest(EmailAddress);
        }

        clock.Advance(TimeSpan.FromMinutes(59));
        limiter.TryCountRequest(EmailAddress);
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.True(limiter.TryCountRequest(EmailAddress));
    }

    // Plusieurs requêtes arrivent en même temps : la limite doit rester exacte.
    [Fact]
    public async Task SimultaneousRequestsNeverExceedTheLimit()
    {
        var limiter = CreateLimiter();
        var requests = new List<Task<bool>>();

        for (var requestNumber = 1; requestNumber <= 200; requestNumber++)
        {
            requests.Add(Task.Run(() =>
                limiter.TryCountRequest(EmailAddress)));
        }

        var results = await Task.WhenAll(requests);

        var acceptedCount = 0;
        foreach (var accepted in results)
        {
            if (accepted)
            {
                acceptedCount = acceptedCount + 1;
            }
        }

        Assert.Equal(3, acceptedCount);
    }

    // Beaucoup de requêtes simultanées sur des adresses différentes : sans verrou, les compteurs se corrompraient.
    [Fact]
    public async Task SimultaneousRequestsOnManyAddressesKeepEveryCounterExact()
    {
        var limiter = CreateLimiter();
        var tasks = new List<Task>();

        for (var taskNumber = 1; taskNumber <= 8; taskNumber++)
        {
            var addressPrefix = "visiteur-" + taskNumber + "-";

            tasks.Add(Task.Run(() =>
            {
                for (var addressNumber = 1; addressNumber <= 500; addressNumber++)
                {
                    limiter.TryCountRequest(addressPrefix + addressNumber + "@exemple.test");
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Chaque adresse a été comptée une fois : il lui reste exactement 2 demandes.
        Assert.True(limiter.TryCountRequest("visiteur-1-1@exemple.test"));
        Assert.True(limiter.TryCountRequest("visiteur-1-1@exemple.test"));
        Assert.False(limiter.TryCountRequest("visiteur-1-1@exemple.test"));
    }
}
