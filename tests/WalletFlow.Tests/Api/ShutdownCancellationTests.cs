using WalletFlow.Api.BackgroundTasks;

namespace WalletFlow.Tests.Api;

// Vérifie que les tâches de fond reconnaissent l'arrêt de l'API, sans le confondre avec une panne.
public class ShutdownCancellationTests
{
    [Fact]
    public void CancellationDuringTheShutdownIsTheShutdown()
    {
        Assert.True(ShutdownCancellation.IsShutdown(new OperationCanceledException(), new CancellationToken(true)));
        Assert.True(ShutdownCancellation.IsShutdown(new TaskCanceledException(), new CancellationToken(true)));
    }

    // Un prestataire qui ne répond pas à temps lève aussi une annulation : sans arrêt demandé, c'est une panne à noter.
    [Fact]
    public void DelayExceededWithoutShutdownIsAFailure()
    {
        Assert.False(ShutdownCancellation.IsShutdown(new TaskCanceledException(), CancellationToken.None));
    }

    [Fact]
    public void OtherFailureDuringTheShutdownIsStillAFailure()
    {
        Assert.False(ShutdownCancellation.IsShutdown(new InvalidOperationException(), new CancellationToken(true)));
    }
}
