using System.Net;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Persistence;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie la demande de récupération (GR Auth, SS §2 et §6). Ce service n'écrit que dans la file d'e-mails, et n'y lit rien :
// il ne peut donc rien révéler sur les comptes qui existent.
public class PasswordRecoveryRequestServiceTests
{
    private static readonly IPAddress IpAddress = IPAddress.Parse("203.0.113.7");

    private readonly FakePasswordRecoveryRequestLimiter requestLimiter = new FakePasswordRecoveryRequestLimiter();
    private readonly FakeEmailRequestRepository emailRequests = new FakeEmailRequestRepository();
    private readonly FakeTimeProvider clock = new FakeTimeProvider();

    // Crée le service à tester, avec ses faux outils.
    private PasswordRecoveryRequestService CreateRecoveryRequestService()
    {
        return new PasswordRecoveryRequestService(
            requestLimiter,
            emailRequests,
            new DatabaseChangesSavingService(new FakeDatabaseUnitOfWork(emailRequests)),
            clock);
    }

    // La demande ne vise aucun compte : elle entre dans la file avant de savoir si l'adresse en a un (DD §9.4).
    [Fact]
    public async Task AcceptedRequestIsQueuedWithTheStoredAddressAndTheIpAddressButNoAccount()
    {
        var result = await CreateRecoveryRequestService().RequestAsync(" Visiteur@Exemple.TEST ", IpAddress);

        Assert.IsType<PasswordRecoveryRequestResult.RequestAccepted>(result);
        var request = Assert.Single(emailRequests.SavedRequests);
        Assert.Equal(EmailRequestType.PasswordRecovery, request.Type);
        Assert.Equal("visiteur@exemple.test", request.EmailAddress);
        Assert.Equal(IpAddress, request.IpAddress);
        Assert.Null(request.UserId);
        Assert.Equal(0, request.FailedTryCount);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, request.RequestedAt);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, request.NextTryAt);
    }

    // Sans la forme enregistrée, changer une majuscule ferait contourner la limite de 3 demandes.
    [Fact]
    public async Task LimitCountsTheAddressInItsStoredForm()
    {
        await CreateRecoveryRequestService().RequestAsync(" Visiteur@Exemple.TEST ", IpAddress);

        Assert.Equal("visiteur@exemple.test", Assert.Single(requestLimiter.CountedAddresses));
    }

    [Theory]
    [InlineData("sans-arobase")]
    [InlineData("nul\0caractere@exemple.test")]
    [InlineData("")]
    public async Task MalformedAddressIsRefusedWithoutBeingCountedNorQueued(string malformedEmailAddress)
    {
        var result = await CreateRecoveryRequestService().RequestAsync(malformedEmailAddress, IpAddress);

        Assert.IsType<PasswordRecoveryRequestResult.InvalidEmailAddressFormat>(result);
        Assert.Empty(requestLimiter.CountedAddresses);
        Assert.Empty(emailRequests.SavedRequests);
    }

    [Fact]
    public async Task RequestOverTheLimitIsRefusedAndNeverQueued()
    {
        requestLimiter.RefusesEveryRequest = true;

        var result = await CreateRecoveryRequestService().RequestAsync("visiteur@exemple.test", IpAddress);

        Assert.IsType<PasswordRecoveryRequestResult.TooManyRequests>(result);
        Assert.Empty(emailRequests.SavedRequests);
    }

    // Une panne de la base n'est pas cachée : la demande serait perdue sans que personne le sache.
    [Fact]
    public async Task DatabaseFailureIsNotHidden()
    {
        emailRequests.IsDown = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateRecoveryRequestService().RequestAsync("visiteur@exemple.test", IpAddress));

        Assert.Empty(emailRequests.SavedRequests);
    }

    // La demande réveille la tâche de fond, une fois enregistrée : l'e-mail part sans attendre son passage suivant.
    [Fact]
    public async Task AcceptedRequestWakesTheBackgroundTaskUp()
    {
        await CreateRecoveryRequestService().RequestAsync("visiteur@exemple.test", IpAddress);

        Assert.True(await emailRequests.WakeUp.WaitAsync(TimeSpan.Zero, CancellationToken.None));
    }
}
