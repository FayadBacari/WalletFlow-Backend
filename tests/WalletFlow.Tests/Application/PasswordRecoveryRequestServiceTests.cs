using System.Net;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie la demande de récupération (GR Auth, SS §2 et §6). Ce service ne reçoit aucun accès à la base :
// il ne peut donc rien révéler sur les comptes qui existent.
public class PasswordRecoveryRequestServiceTests
{
    private static readonly IPAddress IpAddress = IPAddress.Parse("203.0.113.7");

    private readonly FakePasswordRecoveryRequestLimiter requestLimiter = new FakePasswordRecoveryRequestLimiter();
    private readonly FakeBackgroundEmailRequestQueue emailQueue = new FakeBackgroundEmailRequestQueue();

    // Crée le service à tester, avec ses faux outils.
    private PasswordRecoveryRequestService CreateRecoveryRequestService()
    {
        return new PasswordRecoveryRequestService(requestLimiter, emailQueue);
    }

    [Fact]
    public void AcceptedRequestIsQueuedWithTheStoredAddressAndTheIpAddress()
    {
        var result = CreateRecoveryRequestService().Request(" Visiteur@Exemple.TEST ", IpAddress);

        Assert.Equal(PasswordRecoveryRequestResult.RequestAccepted, result);
        var request = Assert.IsType<BackgroundEmailRequest.PasswordRecovery>(Assert.Single(emailQueue.AddedRequests));
        Assert.Equal("visiteur@exemple.test", request.EmailAddress);
        Assert.Equal(IpAddress, request.IpAddress);
    }

    // Sans la forme enregistrée, changer une majuscule ferait contourner la limite de 3 demandes.
    [Fact]
    public void LimitCountsTheAddressInItsStoredForm()
    {
        CreateRecoveryRequestService().Request(" Visiteur@Exemple.TEST ", IpAddress);

        Assert.Equal("visiteur@exemple.test", Assert.Single(requestLimiter.CountedAddresses));
    }

    [Theory]
    [InlineData("sans-arobase")]
    [InlineData("nul\0caractere@exemple.test")]
    [InlineData("")]
    public void MalformedAddressIsRefusedWithoutBeingCountedNorQueued(string malformedEmailAddress)
    {
        var result = CreateRecoveryRequestService().Request(malformedEmailAddress, IpAddress);

        Assert.Equal(PasswordRecoveryRequestResult.InvalidEmailAddressFormat, result);
        Assert.Empty(requestLimiter.CountedAddresses);
        Assert.Empty(emailQueue.AddedRequests);
    }

    [Fact]
    public void RequestOverTheLimitIsRefusedAndNeverQueued()
    {
        requestLimiter.RefusesEveryRequest = true;

        var result = CreateRecoveryRequestService().Request("visiteur@exemple.test", IpAddress);

        Assert.Equal(PasswordRecoveryRequestResult.TooManyRequests, result);
        Assert.Empty(emailQueue.AddedRequests);
    }
}
