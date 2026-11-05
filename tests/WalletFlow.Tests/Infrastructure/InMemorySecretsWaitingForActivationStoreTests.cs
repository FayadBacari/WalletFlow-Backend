using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la mémoire des secrets qui attendent le premier code (GR Auth).
public class InMemorySecretsWaitingForActivationStoreTests
{
    private const string SecretKeptInMemoryInTheseTests = "secret-de-ces-tests";

    private static readonly Guid IdentifierOfTheAccountInTheseTests = Guid.NewGuid();

    private readonly FakeTimeProviderForTests clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();
    private readonly InMemorySecretsWaitingForActivationStore storeBeingTestedWithTheFakeClock;

    // Donne au magasin la fausse horloge.
    public InMemorySecretsWaitingForActivationStoreTests()
    {
        storeBeingTestedWithTheFakeClock = new InMemorySecretsWaitingForActivationStore(clockThatOnlyMovesWhenTestsAskIt);
    }

    [Fact]
    public void SecretOfTheAccountIsFoundAgain()
    {
        storeBeingTestedWithTheFakeClock.SaveSecretWaitingForActivationOfThisAccount(IdentifierOfTheAccountInTheseTests, SecretKeptInMemoryInTheseTests);

        Assert.Equal(SecretKeptInMemoryInTheseTests, storeBeingTestedWithTheFakeClock.FindSecretWaitingForActivationOfThisAccount(IdentifierOfTheAccountInTheseTests));
    }

    [Fact]
    public void SecretOfOneAccountIsNotGivenToAnother()
    {
        storeBeingTestedWithTheFakeClock.SaveSecretWaitingForActivationOfThisAccount(IdentifierOfTheAccountInTheseTests, SecretKeptInMemoryInTheseTests);

        Assert.Null(storeBeingTestedWithTheFakeClock.FindSecretWaitingForActivationOfThisAccount(Guid.NewGuid()));
    }

    [Fact]
    public void NewSecretReplacesThePreviousOne()
    {
        storeBeingTestedWithTheFakeClock.SaveSecretWaitingForActivationOfThisAccount(IdentifierOfTheAccountInTheseTests, SecretKeptInMemoryInTheseTests);
        storeBeingTestedWithTheFakeClock.SaveSecretWaitingForActivationOfThisAccount(IdentifierOfTheAccountInTheseTests, "nouveau-secret");

        Assert.Equal("nouveau-secret", storeBeingTestedWithTheFakeClock.FindSecretWaitingForActivationOfThisAccount(IdentifierOfTheAccountInTheseTests));
    }

    [Fact]
    public void WaitEndsExactlyAfterTenMinutes()
    {
        storeBeingTestedWithTheFakeClock.SaveSecretWaitingForActivationOfThisAccount(IdentifierOfTheAccountInTheseTests, SecretKeptInMemoryInTheseTests);

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(10) - TimeSpan.FromTicks(1));
        Assert.Equal(SecretKeptInMemoryInTheseTests, storeBeingTestedWithTheFakeClock.FindSecretWaitingForActivationOfThisAccount(IdentifierOfTheAccountInTheseTests));

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromTicks(1));
        Assert.Null(storeBeingTestedWithTheFakeClock.FindSecretWaitingForActivationOfThisAccount(IdentifierOfTheAccountInTheseTests));
    }

    [Fact]
    public void RemovedSecretIsNotFoundAndRemovingItTwiceIsHarmless()
    {
        storeBeingTestedWithTheFakeClock.SaveSecretWaitingForActivationOfThisAccount(IdentifierOfTheAccountInTheseTests, SecretKeptInMemoryInTheseTests);

        storeBeingTestedWithTheFakeClock.RemoveSecretWaitingForActivationOfThisAccount(IdentifierOfTheAccountInTheseTests);
        storeBeingTestedWithTheFakeClock.RemoveSecretWaitingForActivationOfThisAccount(IdentifierOfTheAccountInTheseTests);

        Assert.Null(storeBeingTestedWithTheFakeClock.FindSecretWaitingForActivationOfThisAccount(IdentifierOfTheAccountInTheseTests));
    }

    // Beaucoup de secrets simultanés : sans verrou, le dictionnaire se corromprait.
    [Fact]
    public async Task SimultaneousSavesKeepEverySecretExact()
    {
        var tasksSavingSecretsAtTheSameTime = new List<Task>();
        var identifierOfTheLastAccountSaved = Guid.NewGuid();

        for (var numberOfTheTaskBeingStarted = 1; numberOfTheTaskBeingStarted <= 8; numberOfTheTaskBeingStarted++)
        {
            tasksSavingSecretsAtTheSameTime.Add(Task.Run(() =>
            {
                for (var numberOfTheSecretBeingSaved = 1; numberOfTheSecretBeingSaved <= 500; numberOfTheSecretBeingSaved++)
                {
                    storeBeingTestedWithTheFakeClock.SaveSecretWaitingForActivationOfThisAccount(Guid.NewGuid(), SecretKeptInMemoryInTheseTests);
                }
            }));
        }

        await Task.WhenAll(tasksSavingSecretsAtTheSameTime);
        storeBeingTestedWithTheFakeClock.SaveSecretWaitingForActivationOfThisAccount(identifierOfTheLastAccountSaved, SecretKeptInMemoryInTheseTests);

        Assert.Equal(SecretKeptInMemoryInTheseTests, storeBeingTestedWithTheFakeClock.FindSecretWaitingForActivationOfThisAccount(identifierOfTheLastAccountSaved));
        Assert.Equal(4001, storeBeingTestedWithTheFakeClock.NumberOfWaitingSecretsKeptInMemory);
    }

    // Les activations abandonnées ne restent pas en mémoire : le nombre d'entrées retombe dès la prochaine activation.
    [Fact]
    public void AbandonedSecretsAreForgottenAtTheNextNewActivation()
    {
        for (var numberOfTheSecretBeingSaved = 1; numberOfTheSecretBeingSaved <= 100; numberOfTheSecretBeingSaved++)
        {
            storeBeingTestedWithTheFakeClock.SaveSecretWaitingForActivationOfThisAccount(Guid.NewGuid(), SecretKeptInMemoryInTheseTests);
        }

        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(10));
        storeBeingTestedWithTheFakeClock.SaveSecretWaitingForActivationOfThisAccount(IdentifierOfTheAccountInTheseTests, SecretKeptInMemoryInTheseTests);

        Assert.Equal(1, storeBeingTestedWithTheFakeClock.NumberOfWaitingSecretsKeptInMemory);
    }
}
