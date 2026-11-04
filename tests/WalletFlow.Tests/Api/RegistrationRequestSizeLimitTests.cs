using System.Net;
using System.Text;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la limite de taille des requêtes avec le vrai serveur Kestrel : le serveur de test ne l'applique pas.
public class RegistrationRequestSizeLimitTests
{
    // Une requête trop grosse reçoit 413, sans détail technique, et n'arrive jamais à la base.
    [Fact]
    public async Task RequestBiggerThanTheLimitIsRefusedWithPayloadTooLarge()
    {
        using var apiStartedOnTheRealKestrelServer = new ApiFactoryRunningLikeInProduction();
        apiStartedOnTheRealKestrelServer.UseKestrel();
        var clientThatCallsTheRealKestrelServer = apiStartedOnTheRealKestrelServer.CreateClient();
        var bodyBiggerThanTheRequestSizeLimit = "{\"email\":\"" + new string('a', 5000) + "@exemple.fr\",\"password\":\"" + ValidPasswordChosenInTheseTests + "\"}";

        var answerToTheRequestThatIsTooBig = await clientThatCallsTheRealKestrelServer.PostAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new StringContent(bodyBiggerThanTheRequestSizeLimit, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, answerToTheRequestThatIsTooBig.StatusCode);
        var textOfTheAnswerToTheRequestThatIsTooBig = await answerToTheRequestThatIsTooBig.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Request body too large", textOfTheAnswerToTheRequestThatIsTooBig);
        Assert.DoesNotContain("4096", textOfTheAnswerToTheRequestThatIsTooBig);
        Assert.Empty(apiStartedOnTheRealKestrelServer.RepositoryThatKeepsAccountsInMemory.SavedUsers);
        SecurityHeadersAssertions.CheckThatAnswerCarriesTheExactSecurityHeaders(answerToTheRequestThatIsTooBig);
    }

    // La plus longue adresse et le plus long mot de passe permis passent, même si chaque emoji est écrit en \uXXXX.
    [Fact]
    public async Task BiggestValidRequestStaysUnderTheLimit()
    {
        using var apiStartedOnTheRealKestrelServer = new ApiFactoryRunningLikeInProduction();
        apiStartedOnTheRealKestrelServer.UseKestrel();
        var clientThatCallsTheRealKestrelServer = apiStartedOnTheRealKestrelServer.CreateClient();
        var longestEmailAddressAllowedByTheRules = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 58) + ".fr";
        var longestValidPasswordWrittenWithEscapes = "Aa1!" + string.Concat(Enumerable.Repeat("\\ud83d\\ude00", 124));
        var bodyOfTheBiggestValidRequest = "{\"email\":\"" + longestEmailAddressAllowedByTheRules + "\",\"password\":\"" + longestValidPasswordWrittenWithEscapes + "\"}";

        var answerToTheBiggestValidRequest = await clientThatCallsTheRealKestrelServer.PostAsync(
            AddressOfTheAccountRegistrationEndpoint,
            new StringContent(bodyOfTheBiggestValidRequest, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, answerToTheBiggestValidRequest.StatusCode);
        Assert.Single(apiStartedOnTheRealKestrelServer.RepositoryThatKeepsAccountsInMemory.SavedUsers);
    }
}
