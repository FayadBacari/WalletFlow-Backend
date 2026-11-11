using Microsoft.Extensions.Logging;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Infrastructure.Emails;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la file des e-mails préparés en arrière-plan.
public class InMemoryBackgroundEmailRequestQueueTests
{
    private readonly MemoryLoggerProvider loggerProvider = new MemoryLoggerProvider();

    // Crée la file à tester, avec un journal technique qui garde ses messages.
    private InMemoryBackgroundEmailRequestQueue CreateQueueWithLog()
    {
        var loggerFactory = new LoggerFactory(new ILoggerProvider[] { loggerProvider });

        return new InMemoryBackgroundEmailRequestQueue(new Logger<InMemoryBackgroundEmailRequestQueue>(loggerFactory));
    }

    // Crée une demande de récupération pour cette adresse.
    private static BackgroundEmailRequest CreateRecoveryRequest(string emailAddress)
    {
        return new BackgroundEmailRequest.PasswordRecovery
        {
            EmailAddress = emailAddress
        };
    }

    [Fact]
    public async Task RequestsComeOutInTheOrderTheyWentIn()
    {
        var queue = CreateQueueWithLog();
        queue.Add(CreateRecoveryRequest("premier@exemple.test"));
        queue.Add(CreateRecoveryRequest("second@exemple.test"));

        var firstRequest = await queue.WaitForNextAsync(CancellationToken.None);
        var secondRequest = await queue.WaitForNextAsync(CancellationToken.None);

        Assert.Equal("premier@exemple.test", Assert.IsType<BackgroundEmailRequest.PasswordRecovery>(firstRequest).EmailAddress);
        Assert.Equal("second@exemple.test", Assert.IsType<BackgroundEmailRequest.PasswordRecovery>(secondRequest).EmailAddress);
    }

    // La mémoire ne peut pas grossir sans fin : au-delà de la limite, la demande est perdue et la panne est notée.
    [Fact]
    public async Task FullQueueLosesTheRequestAndWritesTheFailureWithoutTheAddress()
    {
        var queue = CreateQueueWithLog();

        for (var requestNumber = 1; requestNumber <= InMemoryBackgroundEmailRequestQueue.QueueCapacity; requestNumber++)
        {
            queue.Add(CreateRecoveryRequest("garde@exemple.test"));
        }

        Assert.Empty(loggerProvider.Messages);

        queue.Add(CreateRecoveryRequest("perdue@exemple.test"));

        var message = Assert.Single(loggerProvider.Messages);
        Assert.StartsWith("Error", message);
        Assert.Contains("pleine", message);
        Assert.DoesNotContain("perdue@exemple.test", message);

        var takenCount = 0;
        for (var requestNumber = 1; requestNumber <= InMemoryBackgroundEmailRequestQueue.QueueCapacity; requestNumber++)
        {
            var request = await queue.WaitForNextAsync(CancellationToken.None);
            Assert.Equal("garde@exemple.test", Assert.IsType<BackgroundEmailRequest.PasswordRecovery>(request).EmailAddress);
            takenCount = takenCount + 1;
        }

        Assert.Equal(1000, takenCount);
    }

    // La tâche de fond attend sans consommer de ressources, et elle est réveillée par la demande suivante.
    [Fact]
    public async Task WaitingForARequestEndsWhenARequestArrives()
    {
        var queue = CreateQueueWithLog();
        var waitTask = queue.WaitForNextAsync(CancellationToken.None).AsTask();

        Assert.False(waitTask.IsCompleted);

        queue.Add(CreateRecoveryRequest("visiteur@exemple.test"));
        var request = await waitTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("visiteur@exemple.test", Assert.IsType<BackgroundEmailRequest.PasswordRecovery>(request).EmailAddress);
    }

    // L'arrêt de l'API doit interrompre l'attente, sinon elle ne s'arrêterait jamais proprement.
    [Fact]
    public async Task StoppingTheApiInterruptsTheWait()
    {
        var queue = CreateQueueWithLog();
        using var cancellationSource = new CancellationTokenSource();
        var waitTask = queue.WaitForNextAsync(cancellationSource.Token).AsTask();

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask);
    }
}
