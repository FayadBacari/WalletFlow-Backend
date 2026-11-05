using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la mémoire des codes déjà utilisés : un code ne sert qu'une seule fois (SS §2).
public class InMemoryUsedAuthenticatorCodesRegisterTests
{
    private const long PeriodNumberUsedInTheseTests = 59796000;

    private static readonly Guid IdentifierOfTheAccountInTheseTests = Guid.NewGuid();

    private readonly FakeTimeProviderForTests clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();
    private readonly InMemoryUsedAuthenticatorCodesRegister registerBeingTestedWithTheFakeClock;

    // Donne au registre la fausse horloge.
    public InMemoryUsedAuthenticatorCodesRegisterTests()
    {
        registerBeingTestedWithTheFakeClock = new InMemoryUsedAuthenticatorCodesRegister(clockThatOnlyMovesWhenTestsAskIt);
    }

    [Fact]
    public void FirstUseOfAPeriodIsAcceptedAndTheSecondIsRefused()
    {
        Assert.True(registerBeingTestedWithTheFakeClock.TryToRecordThisPeriodAsUsedByThisAccount(IdentifierOfTheAccountInTheseTests, PeriodNumberUsedInTheseTests));
        Assert.False(registerBeingTestedWithTheFakeClock.TryToRecordThisPeriodAsUsedByThisAccount(IdentifierOfTheAccountInTheseTests, PeriodNumberUsedInTheseTests));
    }

    [Fact]
    public void OlderPeriodIsRefusedAndNewerPeriodIsAccepted()
    {
        registerBeingTestedWithTheFakeClock.TryToRecordThisPeriodAsUsedByThisAccount(IdentifierOfTheAccountInTheseTests, PeriodNumberUsedInTheseTests);

        Assert.False(registerBeingTestedWithTheFakeClock.TryToRecordThisPeriodAsUsedByThisAccount(IdentifierOfTheAccountInTheseTests, PeriodNumberUsedInTheseTests - 1));
        Assert.True(registerBeingTestedWithTheFakeClock.TryToRecordThisPeriodAsUsedByThisAccount(IdentifierOfTheAccountInTheseTests, PeriodNumberUsedInTheseTests + 1));
    }

    [Fact]
    public void TwoAccountsDoNotShareTheirUsedPeriods()
    {
        registerBeingTestedWithTheFakeClock.TryToRecordThisPeriodAsUsedByThisAccount(IdentifierOfTheAccountInTheseTests, PeriodNumberUsedInTheseTests);

        Assert.True(registerBeingTestedWithTheFakeClock.TryToRecordThisPeriodAsUsedByThisAccount(Guid.NewGuid(), PeriodNumberUsedInTheseTests));
    }

    // Un code reste valable 90 secondes au plus après son utilisation : la mémoire doit durer plus longtemps que lui.
    [Fact]
    public void MemoryOfAUsedCodeLastsLongerThanTheCodeItself()
    {
        registerBeingTestedWithTheFakeClock.TryToRecordThisPeriodAsUsedByThisAccount(IdentifierOfTheAccountInTheseTests, PeriodNumberUsedInTheseTests);

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(InMemoryUsedAuthenticatorCodesRegister.DurationOfTheMemoryOfOneUsedCode - TimeSpan.FromTicks(1));

        Assert.True(InMemoryUsedAuthenticatorCodesRegister.DurationOfTheMemoryOfOneUsedCode > TimeSpan.FromSeconds(90));
        Assert.False(registerBeingTestedWithTheFakeClock.TryToRecordThisPeriodAsUsedByThisAccount(IdentifierOfTheAccountInTheseTests, PeriodNumberUsedInTheseTests));
    }

    // Les comptes inactifs ne restent pas en mémoire : le nombre d'entrées retombe dès le prochain code utilisé.
    [Fact]
    public void OldUsedCodesAreForgottenAtTheNextUse()
    {
        for (var numberOfTheAccountBeingRecorded = 1; numberOfTheAccountBeingRecorded <= 100; numberOfTheAccountBeingRecorded++)
        {
            registerBeingTestedWithTheFakeClock.TryToRecordThisPeriodAsUsedByThisAccount(Guid.NewGuid(), PeriodNumberUsedInTheseTests);
        }

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(InMemoryUsedAuthenticatorCodesRegister.DurationOfTheMemoryOfOneUsedCode);
        registerBeingTestedWithTheFakeClock.TryToRecordThisPeriodAsUsedByThisAccount(IdentifierOfTheAccountInTheseTests, PeriodNumberUsedInTheseTests);

        Assert.Equal(1, registerBeingTestedWithTheFakeClock.NumberOfUsedCodesKeptInMemory);
    }

    // De 100 requêtes simultanées avec le même code, une seule réussit.
    [Fact]
    public async Task SimultaneousRequestsWithTheSameCodeLetOnlyOneSucceed()
    {
        var simultaneousCodeRequestsOfThisRegisterTest = new List<Task<bool>>();

        for (var numberOfTheRequestBeingStarted = 1; numberOfTheRequestBeingStarted <= 100; numberOfTheRequestBeingStarted++)
        {
            simultaneousCodeRequestsOfThisRegisterTest.Add(Task.Run(() => registerBeingTestedWithTheFakeClock.TryToRecordThisPeriodAsUsedByThisAccount(IdentifierOfTheAccountInTheseTests, PeriodNumberUsedInTheseTests)));
        }

        var answersGivenToTheSimultaneousRequests = await Task.WhenAll(simultaneousCodeRequestsOfThisRegisterTest);

        Assert.Equal(1, answersGivenToTheSimultaneousRequests.Count(oneSimultaneousRequestWasAcceptedByTheRegister => oneSimultaneousRequestWasAcceptedByTheRegister));
    }

    // 8 fils partent exactement ensemble, des milliers de fois, avec le même code : sans le verrou, deux fils finissent par réussir.
    [Fact]
    public async Task ThreadsStartingTogetherThousandsOfTimesLetOnlyOneSucceedEachTime()
    {
        const int numberOfRoundsInThisTest = 3000;
        const int numberOfThreadsInEachRound = 8;
        var numberOfRequestsAcceptedInEachRound = new int[numberOfRoundsInThisTest];
        var numberOfExceptionsRaisedInTheThreads = 0;
        using var barrierThatStartsTheThreadsTogether = new Barrier(numberOfThreadsInEachRound);
        var accountsUsedOneByOneInEachRound = new Guid[numberOfRoundsInThisTest];

        for (var numberOfTheRoundBeingPrepared = 0; numberOfTheRoundBeingPrepared < numberOfRoundsInThisTest; numberOfTheRoundBeingPrepared++)
        {
            accountsUsedOneByOneInEachRound[numberOfTheRoundBeingPrepared] = Guid.NewGuid();
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
                        if (registerBeingTestedWithTheFakeClock.TryToRecordThisPeriodAsUsedByThisAccount(accountsUsedOneByOneInEachRound[numberOfTheRoundBeingPlayed], PeriodNumberUsedInTheseTests))
                        {
                            Interlocked.Increment(ref numberOfRequestsAcceptedInEachRound[numberOfTheRoundBeingPlayed]);
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
        Assert.All(numberOfRequestsAcceptedInEachRound, numberOfRequestsAcceptedInOneRound => Assert.Equal(1, numberOfRequestsAcceptedInOneRound));
    }
}
