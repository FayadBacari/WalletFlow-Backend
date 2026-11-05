using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la mémoire des connexions qui attendent leur code (SS §2 et §6).
public class InMemoryLoginsWaitingForTheirCodeStoreTests
{
    private const string HashOfTheTokenInTheseTests = "empreinte-du-jeton";
    private const string SecureHashOfThePasswordInTheseTests = "empreinte-du-mot-de-passe";

    private static readonly Guid IdentifierOfTheAccountInTheseTests = Guid.NewGuid();

    private readonly FakeTimeProviderForTests clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();
    private readonly InMemoryLoginsWaitingForTheirCodeStore storeBeingTestedWithTheFakeClock;

    // Donne au magasin la fausse horloge.
    public InMemoryLoginsWaitingForTheirCodeStoreTests()
    {
        storeBeingTestedWithTheFakeClock = new InMemoryLoginsWaitingForTheirCodeStore(clockThatOnlyMovesWhenTestsAskIt);
    }

    // Garde la connexion en attente de ces tests.
    private void SaveTheWaitingLoginOfTheseTests()
    {
        storeBeingTestedWithTheFakeClock.SaveNewLoginWaitingForItsCode(HashOfTheTokenInTheseTests, IdentifierOfTheAccountInTheseTests, SecureHashOfThePasswordInTheseTests);
    }

    [Fact]
    public void WaitingLoginGivesItsAccountItsPasswordHashAndItsAttemptNumber()
    {
        SaveTheWaitingLoginOfTheseTests();

        var waitingLoginAtTheFirstAttempt = storeBeingTestedWithTheFakeClock.TryToCountOneCodeAttemptOfThisWaitingLogin(HashOfTheTokenInTheseTests);
        var waitingLoginAtTheSecondAttempt = storeBeingTestedWithTheFakeClock.TryToCountOneCodeAttemptOfThisWaitingLogin(HashOfTheTokenInTheseTests);

        Assert.Equal(IdentifierOfTheAccountInTheseTests, waitingLoginAtTheFirstAttempt!.IdentifierOfTheAccountLoggingIn);
        Assert.Equal(SecureHashOfThePasswordInTheseTests, waitingLoginAtTheFirstAttempt.SecureHashOfThePasswordJustChecked);
        Assert.Equal(1, waitingLoginAtTheFirstAttempt.NumberOfCodesTriedSoFar);
        Assert.Equal(2, waitingLoginAtTheSecondAttempt!.NumberOfCodesTriedSoFar);
    }

    [Fact]
    public void UnknownTokenFindsNothingInTheMemory()
    {
        SaveTheWaitingLoginOfTheseTests();

        Assert.Null(storeBeingTestedWithTheFakeClock.TryToCountOneCodeAttemptOfThisWaitingLogin("autre-empreinte"));
    }

    [Fact]
    public void SixthAttemptOfTheCodeFindsNothingInMemory()
    {
        SaveTheWaitingLoginOfTheseTests();

        for (var numberOfTheAttemptBeingCounted = 1; numberOfTheAttemptBeingCounted <= 5; numberOfTheAttemptBeingCounted++)
        {
            Assert.NotNull(storeBeingTestedWithTheFakeClock.TryToCountOneCodeAttemptOfThisWaitingLogin(HashOfTheTokenInTheseTests));
        }

        Assert.Null(storeBeingTestedWithTheFakeClock.TryToCountOneCodeAttemptOfThisWaitingLogin(HashOfTheTokenInTheseTests));
    }

    [Fact]
    public void WaitEndsExactlyAfterFiveMinutes()
    {
        SaveTheWaitingLoginOfTheseTests();

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));
        Assert.NotNull(storeBeingTestedWithTheFakeClock.TryToCountOneCodeAttemptOfThisWaitingLogin(HashOfTheTokenInTheseTests));

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromTicks(1));
        Assert.Null(storeBeingTestedWithTheFakeClock.TryToCountOneCodeAttemptOfThisWaitingLogin(HashOfTheTokenInTheseTests));
    }

    [Fact]
    public void RemovedLoginFindsNothingAndRemovingItTwiceIsHarmless()
    {
        SaveTheWaitingLoginOfTheseTests();

        storeBeingTestedWithTheFakeClock.RemoveThisLoginWaitingForItsCode(HashOfTheTokenInTheseTests);
        storeBeingTestedWithTheFakeClock.RemoveThisLoginWaitingForItsCode(HashOfTheTokenInTheseTests);

        Assert.Null(storeBeingTestedWithTheFakeClock.TryToCountOneCodeAttemptOfThisWaitingLogin(HashOfTheTokenInTheseTests));
    }

    // Chaque connexion a ses propres essais : un attaquant qui recommence n'use pas ceux du vrai propriétaire, et inversement.
    [Fact]
    public void TwoWaitingLoginsOfTheSameAccountHaveTheirOwnAttempts()
    {
        SaveTheWaitingLoginOfTheseTests();
        storeBeingTestedWithTheFakeClock.SaveNewLoginWaitingForItsCode("autre-empreinte", IdentifierOfTheAccountInTheseTests, SecureHashOfThePasswordInTheseTests);

        for (var numberOfTheAttemptBeingCounted = 1; numberOfTheAttemptBeingCounted <= 5; numberOfTheAttemptBeingCounted++)
        {
            storeBeingTestedWithTheFakeClock.TryToCountOneCodeAttemptOfThisWaitingLogin(HashOfTheTokenInTheseTests);
        }

        Assert.Equal(1, storeBeingTestedWithTheFakeClock.TryToCountOneCodeAttemptOfThisWaitingLogin("autre-empreinte")!.NumberOfCodesTriedSoFar);
    }

    // 100 requêtes simultanées ne doivent pas tester plus de 5 codes (SS §6).
    [Fact]
    public async Task SimultaneousAttemptsNeverTestMoreThanFiveCodes()
    {
        SaveTheWaitingLoginOfTheseTests();
        var simultaneousCodeAttemptsOfThisStoreTest = new List<Task<bool>>();

        for (var numberOfTheAttemptBeingStarted = 1; numberOfTheAttemptBeingStarted <= 100; numberOfTheAttemptBeingStarted++)
        {
            simultaneousCodeAttemptsOfThisStoreTest.Add(Task.Run(() => storeBeingTestedWithTheFakeClock.TryToCountOneCodeAttemptOfThisWaitingLogin(HashOfTheTokenInTheseTests) != null));
        }

        var answersGivenToTheSimultaneousAttempts = await Task.WhenAll(simultaneousCodeAttemptsOfThisStoreTest);

        Assert.Equal(5, answersGivenToTheSimultaneousAttempts.Count(oneSimultaneousAttemptWasAcceptedByTheStore => oneSimultaneousAttemptWasAcceptedByTheStore));
    }

    // 8 fils partent exactement ensemble, des milliers de fois : sans le verrou, deux fils finissent par prendre le même essai.
    [Fact]
    public async Task ThreadsStartingTogetherThousandsOfTimesNeverTestMoreThanFiveCodes()
    {
        const int numberOfRoundsInThisTest = 3000;
        const int numberOfThreadsInEachRound = 8;
        var numberOfAttemptsAcceptedInEachRound = new int[numberOfRoundsInThisTest];
        var numberOfExceptionsRaisedInTheThreads = 0;
        using var barrierThatStartsTheThreadsTogether = new Barrier(numberOfThreadsInEachRound);

        for (var numberOfTheRoundBeingPrepared = 0; numberOfTheRoundBeingPrepared < numberOfRoundsInThisTest; numberOfTheRoundBeingPrepared++)
        {
            storeBeingTestedWithTheFakeClock.SaveNewLoginWaitingForItsCode("empreinte-" + numberOfTheRoundBeingPrepared, IdentifierOfTheAccountInTheseTests, SecureHashOfThePasswordInTheseTests);
        }

        var threadsRunningAllTheRoundsOfThisTest = new List<Task>();
        for (var numberOfTheThreadBeingStarted = 0; numberOfTheThreadBeingStarted < numberOfThreadsInEachRound; numberOfTheThreadBeingStarted++)
        {
            threadsRunningAllTheRoundsOfThisTest.Add(Task.Factory.StartNew(() =>
            {
                for (var numberOfTheRoundBeingPlayed = 0; numberOfTheRoundBeingPlayed < numberOfRoundsInThisTest; numberOfTheRoundBeingPlayed++)
                {
                    barrierThatStartsTheThreadsTogether.SignalAndWait();

                    try
                    {
                        if (storeBeingTestedWithTheFakeClock.TryToCountOneCodeAttemptOfThisWaitingLogin("empreinte-" + numberOfTheRoundBeingPlayed) != null)
                        {
                            Interlocked.Increment(ref numberOfAttemptsAcceptedInEachRound[numberOfTheRoundBeingPlayed]);
                        }
                    }
                    // Quelle que soit l'erreur, ce fil continue : les autres l'attendraient sinon pour toujours, et le test ne finirait jamais.
                    catch (Exception)
                    {
                        Interlocked.Increment(ref numberOfExceptionsRaisedInTheThreads);
                    }
                }
            }, TaskCreationOptions.LongRunning));
        }

        await Task.WhenAll(threadsRunningAllTheRoundsOfThisTest);

        Assert.Equal(0, numberOfExceptionsRaisedInTheThreads);
        Assert.All(numberOfAttemptsAcceptedInEachRound, numberOfAttemptsAcceptedInOneRound => Assert.Equal(5, numberOfAttemptsAcceptedInOneRound));
    }

    // Beaucoup de connexions simultanées : sans verrou, le dictionnaire se corromprait.
    [Fact]
    public async Task SimultaneousSavesKeepEveryWaitingLoginExact()
    {
        var tasksSavingWaitingLoginsAtTheSameTime = new List<Task>();

        for (var numberOfTheTaskBeingStarted = 1; numberOfTheTaskBeingStarted <= 8; numberOfTheTaskBeingStarted++)
        {
            var prefixOfTheHashesOfThisTask = "empreinte-" + numberOfTheTaskBeingStarted + "-";

            tasksSavingWaitingLoginsAtTheSameTime.Add(Task.Run(() =>
            {
                for (var numberOfTheLoginBeingSaved = 1; numberOfTheLoginBeingSaved <= 500; numberOfTheLoginBeingSaved++)
                {
                    storeBeingTestedWithTheFakeClock.SaveNewLoginWaitingForItsCode(prefixOfTheHashesOfThisTask + numberOfTheLoginBeingSaved, IdentifierOfTheAccountInTheseTests, SecureHashOfThePasswordInTheseTests);
                }
            }));
        }

        await Task.WhenAll(tasksSavingWaitingLoginsAtTheSameTime);

        Assert.Equal(1, storeBeingTestedWithTheFakeClock.TryToCountOneCodeAttemptOfThisWaitingLogin("empreinte-8-500")!.NumberOfCodesTriedSoFar);
    }

    // Les connexions abandonnées ne restent pas en mémoire : le nombre d'entrées retombe dès la prochaine connexion.
    [Fact]
    public void AbandonedLoginsAreForgottenAtTheNextNewLogin()
    {
        for (var numberOfTheLoginBeingSaved = 1; numberOfTheLoginBeingSaved <= 100; numberOfTheLoginBeingSaved++)
        {
            storeBeingTestedWithTheFakeClock.SaveNewLoginWaitingForItsCode("empreinte-" + numberOfTheLoginBeingSaved, IdentifierOfTheAccountInTheseTests, SecureHashOfThePasswordInTheseTests);
        }

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(5));
        SaveTheWaitingLoginOfTheseTests();

        Assert.Equal(1, storeBeingTestedWithTheFakeClock.NumberOfWaitingLoginsKeptInMemory);
    }
}
