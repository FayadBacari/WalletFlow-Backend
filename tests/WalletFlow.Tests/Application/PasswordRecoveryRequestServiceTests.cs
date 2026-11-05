using System.Net;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie la demande de récupération (GR Auth, SS §2 et §6). Ce service ne reçoit aucun accès à la base :
// il ne peut donc rien révéler sur les comptes qui existent.
public class PasswordRecoveryRequestServiceTests
{
    private static readonly IPAddress IpAddressOfTheVisitorInTheseTests = IPAddress.Parse("203.0.113.7");

    private readonly FakePasswordRecoveryRequestLimiter limiterThatCountsTheEmailAddresses = new FakePasswordRecoveryRequestLimiter();
    private readonly FakeBackgroundEmailRequestQueue queueThatKeepsTheEmailRequestsInMemory = new FakeBackgroundEmailRequestQueue();

    // Crée le service à tester, avec ses faux outils.
    private PasswordRecoveryRequestService CreateRecoveryRequestServiceWithFakeTools()
    {
        return new PasswordRecoveryRequestService(limiterThatCountsTheEmailAddresses, queueThatKeepsTheEmailRequestsInMemory);
    }

    [Fact]
    public void AcceptedRequestIsQueuedWithTheStoredAddressAndTheIpAddress()
    {
        var resultOfThePasswordRecoveryRequest = CreateRecoveryRequestServiceWithFakeTools().RequestPasswordRecoveryForThisEmailAddress(" Visiteur@Exemple.TEST ", IpAddressOfTheVisitorInTheseTests);

        Assert.True(resultOfThePasswordRecoveryRequest.IsAccepted);
        var requestAddedToTheFakeBackgroundQueue = Assert.Single(queueThatKeepsTheEmailRequestsInMemory.RequestsAddedToTheBackgroundQueue);
        Assert.Equal(BackgroundEmailRequestType.PasswordRecovery, requestAddedToTheFakeBackgroundQueue.TypeOfTheRequestedBackgroundEmail);
        Assert.Equal("visiteur@exemple.test", requestAddedToTheFakeBackgroundQueue.EmailAddressOfTheRecoveryRequest);
        Assert.Equal(IpAddressOfTheVisitorInTheseTests, requestAddedToTheFakeBackgroundQueue.IpAddressOfTheRequestingVisitor);
        Assert.Null(requestAddedToTheFakeBackgroundQueue.IdentifierOfTheAccountJustLocked);
    }

    // Sans la forme enregistrée, changer une majuscule ferait contourner la limite de 3 demandes.
    [Fact]
    public void LimitCountsTheAddressInItsStoredForm()
    {
        CreateRecoveryRequestServiceWithFakeTools().RequestPasswordRecoveryForThisEmailAddress(" Visiteur@Exemple.TEST ", IpAddressOfTheVisitorInTheseTests);

        Assert.Equal("visiteur@exemple.test", Assert.Single(limiterThatCountsTheEmailAddresses.EmailAddressesCounted));
    }

    [Theory]
    [InlineData("sans-arobase")]
    [InlineData("nul\0caractere@exemple.test")]
    [InlineData("")]
    public void MalformedAddressIsRefusedWithoutBeingCountedNorQueued(string malformedEmailAddressTypedInThisTest)
    {
        var resultOfThePasswordRecoveryRequest = CreateRecoveryRequestServiceWithFakeTools().RequestPasswordRecoveryForThisEmailAddress(malformedEmailAddressTypedInThisTest, IpAddressOfTheVisitorInTheseTests);

        Assert.False(resultOfThePasswordRecoveryRequest.IsAccepted);
        Assert.Equal(AccountRegistrationService.ErrorMessageForInvalidEmailAddressFormat, resultOfThePasswordRecoveryRequest.EmailAddressErrorMessage);
        Assert.Empty(limiterThatCountsTheEmailAddresses.EmailAddressesCounted);
        Assert.Empty(queueThatKeepsTheEmailRequestsInMemory.RequestsAddedToTheBackgroundQueue);
    }

    [Fact]
    public void RequestOverTheLimitIsRefusedAndNeverQueued()
    {
        limiterThatCountsTheEmailAddresses.EveryRequestIsRefusedDuringThisTest = true;

        var resultOfThePasswordRecoveryRequest = CreateRecoveryRequestServiceWithFakeTools().RequestPasswordRecoveryForThisEmailAddress("visiteur@exemple.test", IpAddressOfTheVisitorInTheseTests);

        Assert.False(resultOfThePasswordRecoveryRequest.IsAccepted);
        Assert.True(resultOfThePasswordRecoveryRequest.TooManyRequestsForThisEmailAddress);
        Assert.Empty(queueThatKeepsTheEmailRequestsInMemory.RequestsAddedToTheBackgroundQueue);
    }
}
