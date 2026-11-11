using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la mémoire des secrets qui attendent le premier code (GR Auth).
public class InMemoryWaitingSecretStoreTests
{
    private const string Secret = "secret-de-ces-tests";

    private static readonly Guid AccountId = Guid.NewGuid();

    private readonly FakeTimeProvider clock = new FakeTimeProvider();
    private readonly InMemoryWaitingSecretStore store;

    // Donne au magasin la fausse horloge.
    public InMemoryWaitingSecretStoreTests()
    {
        store = new InMemoryWaitingSecretStore(clock);
    }

    [Fact]
    public void SecretOfTheAccountIsFoundAgain()
    {
        store.Save(AccountId, Secret);

        Assert.Equal(Secret, store.Find(AccountId));
    }

    [Fact]
    public void SecretOfOneAccountIsNotGivenToAnother()
    {
        store.Save(AccountId, Secret);

        Assert.Null(store.Find(Guid.NewGuid()));
    }

    [Fact]
    public void NewSecretReplacesThePreviousOne()
    {
        store.Save(AccountId, Secret);
        store.Save(AccountId, "nouveau-secret");

        Assert.Equal("nouveau-secret", store.Find(AccountId));
    }

    [Fact]
    public void WaitEndsExactlyAfterTenMinutes()
    {
        store.Save(AccountId, Secret);

        clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromTicks(1));
        Assert.Equal(Secret, store.Find(AccountId));

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Null(store.Find(AccountId));
    }

    [Fact]
    public void RemovedSecretIsNotFoundAndRemovingItTwiceIsHarmless()
    {
        store.Save(AccountId, Secret);

        store.Remove(AccountId);
        store.Remove(AccountId);

        Assert.Null(store.Find(AccountId));
    }

    // Beaucoup de secrets simultanés : sans verrou, le dictionnaire se corromprait.
    [Fact]
    public async Task SimultaneousSavesKeepEverySecretExact()
    {
        var tasks = new List<Task>();
        var lastAccountId = Guid.NewGuid();

        for (var taskNumber = 1; taskNumber <= 8; taskNumber++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (var secretNumber = 1; secretNumber <= 500; secretNumber++)
                {
                    store.Save(Guid.NewGuid(), Secret);
                }
            }));
        }

        await Task.WhenAll(tasks);
        store.Save(lastAccountId, Secret);

        Assert.Equal(Secret, store.Find(lastAccountId));
        Assert.Equal(4001, store.WaitingSecretCount);
    }

    // Les activations abandonnées ne restent pas en mémoire : le nombre d'entrées retombe dès la prochaine activation.
    [Fact]
    public void AbandonedSecretsAreForgottenAtTheNextNewActivation()
    {
        for (var secretNumber = 1; secretNumber <= 100; secretNumber++)
        {
            store.Save(Guid.NewGuid(), Secret);
        }

        clock.Advance(TimeSpan.FromMinutes(10));
        store.Save(AccountId, Secret);

        Assert.Equal(1, store.WaitingSecretCount);
    }
}
