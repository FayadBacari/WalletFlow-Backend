using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la mémoire des codes déjà utilisés : un code ne sert qu'une seule fois (SS §2).
public class InMemoryUsedAuthenticatorCodesRegisterTests
{
    private const long Period = 59796000;

    private static readonly Guid AccountId = Guid.NewGuid();

    private readonly FakeTimeProvider clock = new FakeTimeProvider();
    private readonly InMemoryUsedAuthenticatorCodesRegister register;

    // Donne au registre la fausse horloge.
    public InMemoryUsedAuthenticatorCodesRegisterTests()
    {
        register = new InMemoryUsedAuthenticatorCodesRegister(clock);
    }

    [Fact]
    public void FirstUseOfAPeriodIsAcceptedAndTheSecondIsRefused()
    {
        Assert.True(register.TryRecordUse(AccountId, Period));
        Assert.False(register.TryRecordUse(AccountId, Period));
    }

    [Fact]
    public void OlderPeriodIsRefusedAndNewerPeriodIsAccepted()
    {
        register.TryRecordUse(AccountId, Period);

        Assert.False(register.TryRecordUse(AccountId, Period - 1));
        Assert.True(register.TryRecordUse(AccountId, Period + 1));
    }

    [Fact]
    public void TwoAccountsDoNotShareTheirUsedPeriods()
    {
        register.TryRecordUse(AccountId, Period);

        Assert.True(register.TryRecordUse(Guid.NewGuid(), Period));
    }

    // Un code reste valable 90 secondes au plus après son utilisation : la mémoire doit durer plus longtemps que lui.
    [Fact]
    public void MemoryOfAUsedCodeLastsLongerThanTheCodeItself()
    {
        register.TryRecordUse(AccountId, Period);

        clock.Advance(InMemoryUsedAuthenticatorCodesRegister.UsedCodeRetention - TimeSpan.FromTicks(1));

        Assert.True(InMemoryUsedAuthenticatorCodesRegister.UsedCodeRetention > TimeSpan.FromSeconds(90));
        Assert.False(register.TryRecordUse(AccountId, Period));
    }

    // Les comptes inactifs ne restent pas en mémoire : le nombre d'entrées retombe dès le prochain code utilisé.
    [Fact]
    public void OldUsedCodesAreForgottenAtTheNextUse()
    {
        for (var accountNumber = 1; accountNumber <= 100; accountNumber++)
        {
            register.TryRecordUse(Guid.NewGuid(), Period);
        }

        clock.Advance(InMemoryUsedAuthenticatorCodesRegister.UsedCodeRetention);
        register.TryRecordUse(AccountId, Period);

        Assert.Equal(1, register.UsedCodeCount);
    }

    // De 100 requêtes simultanées avec le même code, une seule réussit.
    [Fact]
    public async Task SimultaneousRequestsWithTheSameCodeLetOnlyOneSucceed()
    {
        var requests = new List<Task<bool>>();

        for (var requestNumber = 1; requestNumber <= 100; requestNumber++)
        {
            requests.Add(Task.Run(() => register.TryRecordUse(AccountId, Period)));
        }

        var results = await Task.WhenAll(requests);

        Assert.Equal(1, results.Count(accepted => accepted));
    }

    // 8 fils partent exactement ensemble, des milliers de fois, avec le même code : sans le verrou, deux fils finissent par réussir.
    [Fact]
    public async Task ThreadsStartingTogetherThousandsOfTimesLetOnlyOneSucceedEachTime()
    {
        const int roundCount = 3000;
        const int threadCount = 8;
        var acceptedPerRound = new int[roundCount];
        var exceptionCount = 0;
        using var barrier = new Barrier(threadCount);
        var accounts = new Guid[roundCount];

        for (var round = 0; round < roundCount; round++)
        {
            accounts[round] = Guid.NewGuid();
        }

        var threads = new List<Task>();
        for (var threadNumber = 0; threadNumber < threadCount; threadNumber++)
        {
            threads.Add(Task.Factory.StartNew(() =>
            {
                for (var round = 0; round < roundCount; round++)
                {
                    barrier.SignalAndWait();

                    try
                    {
                        if (register.TryRecordUse(accounts[round], Period))
                        {
                            Interlocked.Increment(ref acceptedPerRound[round]);
                        }
                    }
                    // Quelle que soit l'erreur, ce fil continue : les autres l'attendraient sinon pour toujours, et le test ne finirait jamais.
                    catch (Exception)
                    {
                        Interlocked.Increment(ref exceptionCount);
                    }
                }
            }, TaskCreationOptions.LongRunning));
        }

        await Task.WhenAll(threads);

        Assert.Equal(0, exceptionCount);
        Assert.All(acceptedPerRound, acceptedInRound => Assert.Equal(1, acceptedInRound));
    }
}
