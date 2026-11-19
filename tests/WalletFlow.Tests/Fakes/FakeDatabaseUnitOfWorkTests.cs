using System.Net;
using WalletFlow.Application.Emails;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Tests.Fakes;

// Vérifie que la fausse unité de travail enregistre comme la vraie base : une transaction en entier avant la suivante, et le
// réveil de la file d'e-mails seulement après tout l'enregistrement. Sinon, la tâche de fond des e-mails pouvait lire une
// demande dont la suite n'était pas encore là, et la retirer sans e-mail : un test échouait de temps en temps dans la CI.
public class FakeDatabaseUnitOfWorkTests
{
    private static readonly TimeSpan LongEnough = TimeSpan.FromSeconds(5);

    // Un dépôt témoin, enregistré après la file d'e-mails : il note si le réveil était déjà donné quand vient son tour.
    private sealed class WakeUpWitness(EmailRequestWakeUp wakeUp) : ITransactionalFakeRepository
    {
        public bool? WokenBeforeItsTurn { get; private set; } = null;

        // Aucune table en panne.
        public void ThrowIfTableIsDown()
        {
        }

        // Aucun conflit.
        public bool HasConcurrentChange()
        {
            return false;
        }

        // Rien à annuler.
        public void Rollback()
        {
        }

        // Rien à écrire.
        public void ApplyConcurrentChanges()
        {
        }

        // Regarde, sans attendre, si le réveil est déjà donné.
        public void Commit()
        {
            WokenBeforeItsTurn = wakeUp.WaitAsync(TimeSpan.Zero, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    // Un dépôt dont le premier enregistrement s'arrête au milieu, jusqu'à ce que le test le libère.
    private sealed class PausingRepository(TaskCompletionSource commitStarted, ManualResetEventSlim release) : ITransactionalFakeRepository
    {
        // Aucune table en panne.
        public void ThrowIfTableIsDown()
        {
        }

        // Aucun conflit.
        public bool HasConcurrentChange()
        {
            return false;
        }

        // Rien à annuler.
        public void Rollback()
        {
        }

        // Rien à écrire.
        public void ApplyConcurrentChanges()
        {
        }

        // Le premier enregistrement prévient le test, puis attend d'être libéré ; les suivants passent tout de suite.
        public void Commit()
        {
            if (commitStarted.TrySetResult())
            {
                release.Wait(LongEnough);
            }
        }
    }

    // La tâche de fond n'est réveillée qu'une fois la transaction enregistrée dans tous les dépôts, même ceux qui viennent après
    // la file d'e-mails.
    [Fact]
    public async Task EmailWakeUpComesAfterTheWholeTransaction()
    {
        using var wakeUp = new EmailRequestWakeUp();
        var emailRequests = new FakeEmailRequestRepository(wakeUp);
        var witness = new WakeUpWitness(wakeUp);
        var unitOfWork = new FakeDatabaseUnitOfWork(emailRequests, witness);
        emailRequests.PrepareRequest(EmailRequest.ForNewDevice(Guid.NewGuid(), IPAddress.Loopback, DateTime.UtcNow));

        Assert.True(await unitOfWork.TrySaveChangesAsync());

        Assert.False(witness.WokenBeforeItsTurn);
        Assert.True(await wakeUp.WaitAsync(TimeSpan.Zero, CancellationToken.None));
    }

    // Une transaction sans nouvelle demande ne réveille pas la tâche de fond.
    [Fact]
    public async Task TransactionWithoutNewRequestDoesNotWakeTheTask()
    {
        using var wakeUp = new EmailRequestWakeUp();
        var unitOfWork = new FakeDatabaseUnitOfWork(new FakeEmailRequestRepository(wakeUp));

        Assert.True(await unitOfWork.TrySaveChangesAsync());

        Assert.False(await wakeUp.WaitAsync(TimeSpan.Zero, CancellationToken.None));
    }

    // Une transaction attend la fin de celle en cours : le réveil donné par l'une ne part jamais au milieu de l'autre.
    [Fact]
    public async Task SecondTransactionWaitsForTheFirst()
    {
        var firstCommitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirstCommit = new ManualResetEventSlim();
        var unitOfWork = new FakeDatabaseUnitOfWork(new PausingRepository(firstCommitStarted, releaseFirstCommit));

        var first = Task.Run(() => unitOfWork.TrySaveChangesAsync());
        await firstCommitStarted.Task.WaitAsync(LongEnough);
        var second = Task.Run(() => unitOfWork.TrySaveChangesAsync());
        var secondEndedDuringTheFirst = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(300))) == second;
        releaseFirstCommit.Set();

        Assert.True(await first);
        Assert.True(await second);
        Assert.False(secondEndedDuringTheFirst);
    }
}
