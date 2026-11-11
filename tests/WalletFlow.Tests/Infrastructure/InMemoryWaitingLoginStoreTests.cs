using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la mémoire des connexions qui attendent leur code (SS §2 et §6).
public class InMemoryWaitingLoginStoreTests
{
    private const string TokenHash = "empreinte-du-jeton";
    private const string PasswordHash = "empreinte-du-mot-de-passe";

    private static readonly Guid AccountId = Guid.NewGuid();

    private readonly FakeTimeProvider clock = new FakeTimeProvider();
    private readonly InMemoryWaitingLoginStore store;

    // Donne au magasin la fausse horloge.
    public InMemoryWaitingLoginStoreTests()
    {
        store = new InMemoryWaitingLoginStore(clock);
    }

    // Garde la connexion en attente de ces tests.
    private void SaveWaitingLogin()
    {
        store.Save(TokenHash, AccountId, PasswordHash);
    }

    [Fact]
    public void WaitingLoginGivesItsAccountItsPasswordHashAndItsAttemptNumber()
    {
        SaveWaitingLogin();

        var firstAttemptLogin = store.TryCountCodeAttempt(TokenHash);
        var secondAttemptLogin = store.TryCountCodeAttempt(TokenHash);

        Assert.Equal(AccountId, firstAttemptLogin!.AccountId);
        Assert.Equal(PasswordHash, firstAttemptLogin.PasswordHash);
        Assert.Equal(1, firstAttemptLogin.TriedCodeCount);
        Assert.Equal(2, secondAttemptLogin!.TriedCodeCount);
    }

    [Fact]
    public void UnknownTokenFindsNothingInTheMemory()
    {
        SaveWaitingLogin();

        Assert.Null(store.TryCountCodeAttempt("autre-empreinte"));
    }

    [Fact]
    public void SixthAttemptOfTheCodeFindsNothingInMemory()
    {
        SaveWaitingLogin();

        for (var attemptNumber = 1; attemptNumber <= 5; attemptNumber++)
        {
            Assert.NotNull(store.TryCountCodeAttempt(TokenHash));
        }

        Assert.Null(store.TryCountCodeAttempt(TokenHash));
    }

    [Fact]
    public void WaitEndsExactlyAfterFiveMinutes()
    {
        SaveWaitingLogin();

        clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));
        Assert.NotNull(store.TryCountCodeAttempt(TokenHash));

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Null(store.TryCountCodeAttempt(TokenHash));
    }

    [Fact]
    public void RemovedLoginFindsNothingAndRemovingItTwiceIsHarmless()
    {
        SaveWaitingLogin();

        store.Remove(TokenHash);
        store.Remove(TokenHash);

        Assert.Null(store.TryCountCodeAttempt(TokenHash));
    }

    // Chaque connexion a ses propres essais : un attaquant qui recommence n'use pas ceux du vrai propriétaire, et inversement.
    [Fact]
    public void TwoWaitingLoginsOfTheSameAccountHaveTheirOwnAttempts()
    {
        SaveWaitingLogin();
        store.Save("autre-empreinte", AccountId, PasswordHash);

        for (var attemptNumber = 1; attemptNumber <= 5; attemptNumber++)
        {
            store.TryCountCodeAttempt(TokenHash);
        }

        Assert.Equal(1, store.TryCountCodeAttempt("autre-empreinte")!.TriedCodeCount);
    }

    // 100 requêtes simultanées ne doivent pas tester plus de 5 codes (SS §6).
    [Fact]
    public async Task SimultaneousAttemptsNeverTestMoreThanFiveCodes()
    {
        SaveWaitingLogin();
        var attempts = new List<Task<bool>>();

        for (var attemptNumber = 1; attemptNumber <= 100; attemptNumber++)
        {
            attempts.Add(Task.Run(() => store.TryCountCodeAttempt(TokenHash) != null));
        }

        var results = await Task.WhenAll(attempts);

        Assert.Equal(5, results.Count(accepted => accepted));
    }

    // 8 fils partent exactement ensemble, des milliers de fois : sans le verrou, deux fils finissent par prendre le même essai.
    [Fact]
    public async Task ThreadsStartingTogetherThousandsOfTimesNeverTestMoreThanFiveCodes()
    {
        const int roundCount = 3000;
        const int threadCount = 8;
        var acceptedPerRound = new int[roundCount];
        var exceptionCount = 0;
        using var barrier = new Barrier(threadCount);

        for (var round = 0; round < roundCount; round++)
        {
            store.Save("empreinte-" + round, AccountId, PasswordHash);
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
                        if (store.TryCountCodeAttempt("empreinte-" + round) != null)
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
        Assert.All(acceptedPerRound, acceptedInRound => Assert.Equal(5, acceptedInRound));
    }

    // Beaucoup de connexions simultanées : sans verrou, le dictionnaire se corromprait.
    [Fact]
    public async Task SimultaneousSavesKeepEveryWaitingLoginExact()
    {
        var tasks = new List<Task>();

        for (var taskNumber = 1; taskNumber <= 8; taskNumber++)
        {
            var hashPrefix = "empreinte-" + taskNumber + "-";

            tasks.Add(Task.Run(() =>
            {
                for (var loginNumber = 1; loginNumber <= 500; loginNumber++)
                {
                    store.Save(hashPrefix + loginNumber, AccountId, PasswordHash);
                }
            }));
        }

        await Task.WhenAll(tasks);

        Assert.Equal(1, store.TryCountCodeAttempt("empreinte-8-500")!.TriedCodeCount);
    }

    // Les connexions abandonnées ne restent pas en mémoire : le nombre d'entrées retombe dès la prochaine connexion.
    [Fact]
    public void AbandonedLoginsAreForgottenAtTheNextNewLogin()
    {
        for (var loginNumber = 1; loginNumber <= 100; loginNumber++)
        {
            store.Save("empreinte-" + loginNumber, AccountId, PasswordHash);
        }

        clock.Advance(TimeSpan.FromMinutes(5));
        SaveWaitingLogin();

        Assert.Equal(1, store.WaitingLoginCount);
    }
}
