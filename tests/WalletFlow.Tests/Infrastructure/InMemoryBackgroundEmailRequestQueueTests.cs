using Microsoft.Extensions.Logging;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Infrastructure.Emails;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la file des e-mails préparés en arrière-plan.
public class InMemoryBackgroundEmailRequestQueueTests
{
    private readonly LoggerProviderThatKeepsTheMessages providerThatKeepsTheLogMessages = new LoggerProviderThatKeepsTheMessages();

    // Crée la file à tester, avec un journal technique qui garde ses messages.
    private InMemoryBackgroundEmailRequestQueue CreateQueueWithItsTechnicalLog()
    {
        var factoryOfTheLoggersOfThisTest = new LoggerFactory(new ILoggerProvider[] { providerThatKeepsTheLogMessages });

        return new InMemoryBackgroundEmailRequestQueue(new Logger<InMemoryBackgroundEmailRequestQueue>(factoryOfTheLoggersOfThisTest));
    }

    // Crée une demande de récupération pour cette adresse.
    private static BackgroundEmailRequest CreateRecoveryRequestForThisEmailAddress(string emailAddressOfTheRecoveryRequest)
    {
        return new BackgroundEmailRequest
        {
            TypeOfTheRequestedBackgroundEmail = BackgroundEmailRequestType.PasswordRecovery,
            EmailAddressOfTheRecoveryRequest = emailAddressOfTheRecoveryRequest
        };
    }

    [Fact]
    public async Task RequestsComeOutInTheOrderTheyWentIn()
    {
        var queueBeingTestedWithItsLog = CreateQueueWithItsTechnicalLog();
        queueBeingTestedWithItsLog.AddEmailRequestToTheBackgroundQueue(CreateRecoveryRequestForThisEmailAddress("premier@exemple.test"));
        queueBeingTestedWithItsLog.AddEmailRequestToTheBackgroundQueue(CreateRecoveryRequestForThisEmailAddress("second@exemple.test"));

        var firstRequestOutOfTheQueue = await queueBeingTestedWithItsLog.WaitForNextEmailRequestOfTheQueueAsync(CancellationToken.None);
        var secondRequestOutOfTheQueue = await queueBeingTestedWithItsLog.WaitForNextEmailRequestOfTheQueueAsync(CancellationToken.None);

        Assert.Equal("premier@exemple.test", firstRequestOutOfTheQueue.EmailAddressOfTheRecoveryRequest);
        Assert.Equal("second@exemple.test", secondRequestOutOfTheQueue.EmailAddressOfTheRecoveryRequest);
    }

    // La mémoire ne peut pas grossir sans fin : au-delà de la limite, la demande est perdue et la panne est notée.
    [Fact]
    public async Task FullQueueLosesTheRequestAndWritesTheFailureWithoutTheAddress()
    {
        var queueBeingTestedWithItsLog = CreateQueueWithItsTechnicalLog();

        for (var numberOfTheRequestBeingAdded = 1; numberOfTheRequestBeingAdded <= InMemoryBackgroundEmailRequestQueue.MaximumNumberOfRequestsWaitingInTheQueue; numberOfTheRequestBeingAdded++)
        {
            queueBeingTestedWithItsLog.AddEmailRequestToTheBackgroundQueue(CreateRecoveryRequestForThisEmailAddress("garde@exemple.test"));
        }

        Assert.Empty(providerThatKeepsTheLogMessages.MessagesWrittenInTheLog);

        queueBeingTestedWithItsLog.AddEmailRequestToTheBackgroundQueue(CreateRecoveryRequestForThisEmailAddress("perdue@exemple.test"));

        var messageWrittenInTheTechnicalLog = Assert.Single(providerThatKeepsTheLogMessages.MessagesWrittenInTheLog);
        Assert.StartsWith("Error", messageWrittenInTheTechnicalLog);
        Assert.Contains("pleine", messageWrittenInTheTechnicalLog);
        Assert.DoesNotContain("perdue@exemple.test", messageWrittenInTheTechnicalLog);

        var numberOfRequestsTakenOutOfTheQueue = 0;
        for (var numberOfTheRequestBeingTaken = 1; numberOfTheRequestBeingTaken <= InMemoryBackgroundEmailRequestQueue.MaximumNumberOfRequestsWaitingInTheQueue; numberOfTheRequestBeingTaken++)
        {
            var requestTakenOutOfTheQueue = await queueBeingTestedWithItsLog.WaitForNextEmailRequestOfTheQueueAsync(CancellationToken.None);
            Assert.Equal("garde@exemple.test", requestTakenOutOfTheQueue.EmailAddressOfTheRecoveryRequest);
            numberOfRequestsTakenOutOfTheQueue = numberOfRequestsTakenOutOfTheQueue + 1;
        }

        Assert.Equal(1000, numberOfRequestsTakenOutOfTheQueue);
    }

    // La tâche de fond attend sans consommer de ressources, et elle est réveillée par la demande suivante.
    [Fact]
    public async Task WaitingForARequestEndsWhenARequestArrives()
    {
        var queueBeingTestedWithItsLog = CreateQueueWithItsTechnicalLog();
        var waitForTheNextRequestOfTheQueue = queueBeingTestedWithItsLog.WaitForNextEmailRequestOfTheQueueAsync(CancellationToken.None).AsTask();

        Assert.False(waitForTheNextRequestOfTheQueue.IsCompleted);

        queueBeingTestedWithItsLog.AddEmailRequestToTheBackgroundQueue(CreateRecoveryRequestForThisEmailAddress("visiteur@exemple.test"));
        var requestTakenOutOfTheQueue = await waitForTheNextRequestOfTheQueue.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("visiteur@exemple.test", requestTakenOutOfTheQueue.EmailAddressOfTheRecoveryRequest);
    }

    // L'arrêt de l'API doit interrompre l'attente, sinon elle ne s'arrêterait jamais proprement.
    [Fact]
    public async Task StoppingTheApiInterruptsTheWait()
    {
        var queueBeingTestedWithItsLog = CreateQueueWithItsTechnicalLog();
        using var sourceThatStopsTheWaitForRequests = new CancellationTokenSource();
        var waitForTheNextRequestOfTheQueue = queueBeingTestedWithItsLog.WaitForNextEmailRequestOfTheQueueAsync(sourceThatStopsTheWaitForRequests.Token).AsTask();

        sourceThatStopsTheWaitForRequests.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitForTheNextRequestOfTheQueue);
    }
}
