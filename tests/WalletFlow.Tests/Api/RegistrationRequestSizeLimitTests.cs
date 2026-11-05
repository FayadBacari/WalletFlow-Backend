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

    // La route de confirmation a la même limite : le mot de passe saisi n'arrive jamais au hachage s'il est géant.
    [Fact]
    public async Task ConfirmationRequestBiggerThanTheLimitIsRefusedWithPayloadTooLarge()
    {
        using var apiStartedOnTheRealKestrelServer = new ApiFactoryRunningLikeInProduction();
        apiStartedOnTheRealKestrelServer.UseKestrel();
        var clientThatCallsTheRealKestrelServer = apiStartedOnTheRealKestrelServer.CreateClient();
        var bodyBiggerThanTheRequestSizeLimit = "{\"token\":\"jeton\",\"password\":\"" + new string('a', 5000) + "\"}";

        var answerToTheRequestThatIsTooBig = await clientThatCallsTheRealKestrelServer.PostAsync(
            AddressOfTheAccountConfirmationEndpoint,
            new StringContent(bodyBiggerThanTheRequestSizeLimit, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, answerToTheRequestThatIsTooBig.StatusCode);
    }

    // La route de connexion a la même limite : le mot de passe saisi n'arrive jamais au hachage s'il est géant.
    [Fact]
    public async Task LoginRequestBiggerThanTheLimitIsRefusedWithPayloadTooLarge()
    {
        using var apiStartedOnTheRealKestrelServer = new ApiFactoryRunningLikeInProduction();
        apiStartedOnTheRealKestrelServer.UseKestrel();
        var clientThatCallsTheRealKestrelServer = apiStartedOnTheRealKestrelServer.CreateClient();
        var bodyBiggerThanTheRequestSizeLimit = "{\"email\":\"utilisateur@exemple.test\",\"password\":\"" + new string('a', 5000) + "\"}";

        var answerToTheRequestThatIsTooBig = await clientThatCallsTheRealKestrelServer.PostAsync(
            LoginRequestTestValues.AddressOfTheAccountLoginEndpoint,
            new StringContent(bodyBiggerThanTheRequestSizeLimit, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, answerToTheRequestThatIsTooBig.StatusCode);
        Assert.Empty(apiStartedOnTheRealKestrelServer.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
    }

    // Le plus long mot de passe permis passe sur la route de connexion.
    [Fact]
    public async Task BiggestValidLoginRequestStaysUnderTheLimit()
    {
        using var apiStartedOnTheRealKestrelServer = new ApiFactoryRunningLikeInProduction();
        apiStartedOnTheRealKestrelServer.UseKestrel();
        var clientThatCallsTheRealKestrelServer = apiStartedOnTheRealKestrelServer.CreateClient();
        var longestEmailAddressAllowedByTheRules = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 58) + ".fr";
        var longestValidPasswordWrittenWithEscapes = "Aa1!" + string.Concat(Enumerable.Repeat("\\ud83d\\ude00", 124));
        var bodyOfTheBiggestValidRequest = "{\"email\":\"" + longestEmailAddressAllowedByTheRules + "\",\"password\":\"" + longestValidPasswordWrittenWithEscapes + "\"}";

        var answerToTheBiggestValidRequest = await clientThatCallsTheRealKestrelServer.PostAsync(
            LoginRequestTestValues.AddressOfTheAccountLoginEndpoint,
            new StringContent(bodyOfTheBiggestValidRequest, Encoding.UTF8, "application/json"));

        // Le compte n'existe pas : la requête a donc bien été lue jusqu'au bout, sans erreur de taille.
        Assert.Equal(HttpStatusCode.Unauthorized, answerToTheBiggestValidRequest.StatusCode);
    }

    // Les routes de récupération et de déblocage ont la même limite.
    [Theory]
    [InlineData(RecoveryRequestTestValues.AddressOfTheForgotPasswordEndpoint, "email")]
    [InlineData(RecoveryRequestTestValues.AddressOfTheResetPasswordEndpoint, "newPassword")]
    [InlineData(RecoveryRequestTestValues.AddressOfTheAccountUnlockEndpoint, "token")]
    public async Task RecoveryRequestBiggerThanTheLimitIsRefusedWithPayloadTooLarge(string addressOfTheEndpointBeingTested, string nameOfTheFieldFilledWithLongText)
    {
        using var apiStartedOnTheRealKestrelServer = new ApiFactoryRunningLikeInProduction();
        apiStartedOnTheRealKestrelServer.UseKestrel();
        var clientThatCallsTheRealKestrelServer = apiStartedOnTheRealKestrelServer.CreateClient();
        var bodyBiggerThanTheRequestSizeLimit = "{\"token\":\"jeton\",\"" + nameOfTheFieldFilledWithLongText + "\":\"" + new string('a', 5000) + "\"}";

        var answerToTheRequestThatIsTooBig = await clientThatCallsTheRealKestrelServer.PostAsync(
            addressOfTheEndpointBeingTested,
            new StringContent(bodyBiggerThanTheRequestSizeLimit, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, answerToTheRequestThatIsTooBig.StatusCode);
    }

    // La plus longue adresse permise passe sur la demande de récupération.
    [Fact]
    public async Task BiggestValidForgotPasswordRequestStaysUnderTheLimit()
    {
        using var apiStartedOnTheRealKestrelServer = new ApiFactoryRunningLikeInProduction();
        apiStartedOnTheRealKestrelServer.UseKestrel();
        var clientThatCallsTheRealKestrelServer = apiStartedOnTheRealKestrelServer.CreateClient();
        var longestEmailAddressAllowedByTheRules = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 58) + ".fr";

        var answerToTheBiggestValidRequest = await clientThatCallsTheRealKestrelServer.PostAsync(
            RecoveryRequestTestValues.AddressOfTheForgotPasswordEndpoint,
            new StringContent("{\"email\":\"" + longestEmailAddressAllowedByTheRules + "\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, answerToTheBiggestValidRequest.StatusCode);
    }

    // Le plus long jeton réel et le plus long mot de passe permis passent sur la route du nouveau mot de passe.
    [Fact]
    public async Task BiggestValidResetPasswordRequestStaysUnderTheLimit()
    {
        using var apiStartedOnTheRealKestrelServer = new ApiFactoryRunningLikeInProduction();
        apiStartedOnTheRealKestrelServer.UseKestrel();
        var clientThatCallsTheRealKestrelServer = apiStartedOnTheRealKestrelServer.CreateClient();
        var longestValidPasswordWrittenWithEscapes = "Aa1!" + string.Concat(Enumerable.Repeat("\\ud83d\\ude00", 124));
        var bodyOfTheBiggestValidRequest = "{\"token\":\"" + new string('a', 43) + "\",\"newPassword\":\"" + longestValidPasswordWrittenWithEscapes + "\"}";

        var answerToTheBiggestValidRequest = await clientThatCallsTheRealKestrelServer.PostAsync(
            RecoveryRequestTestValues.AddressOfTheResetPasswordEndpoint,
            new StringContent(bodyOfTheBiggestValidRequest, Encoding.UTF8, "application/json"));

        // Le jeton est inconnu : la requête a donc bien été lue jusqu'au bout, sans erreur de taille.
        Assert.Equal(HttpStatusCode.BadRequest, answerToTheBiggestValidRequest.StatusCode);
    }

    // Le plus long jeton réel et le plus long mot de passe permis passent sur la route de confirmation.
    [Fact]
    public async Task BiggestValidConfirmationRequestStaysUnderTheLimit()
    {
        using var apiStartedOnTheRealKestrelServer = new ApiFactoryRunningLikeInProduction();
        apiStartedOnTheRealKestrelServer.UseKestrel();
        var clientThatCallsTheRealKestrelServer = apiStartedOnTheRealKestrelServer.CreateClient();
        var longestValidPasswordWrittenWithEscapes = "Aa1!" + string.Concat(Enumerable.Repeat("\\ud83d\\ude00", 124));
        var bodyOfTheBiggestValidRequest = "{\"token\":\"" + new string('a', 43) + "\",\"password\":\"" + longestValidPasswordWrittenWithEscapes + "\"}";

        var answerToTheBiggestValidRequest = await clientThatCallsTheRealKestrelServer.PostAsync(
            AddressOfTheAccountConfirmationEndpoint,
            new StringContent(bodyOfTheBiggestValidRequest, Encoding.UTF8, "application/json"));

        // Le jeton est inconnu : la requête a donc bien été lue jusqu'au bout, sans erreur de taille.
        Assert.Equal(HttpStatusCode.BadRequest, answerToTheBiggestValidRequest.StatusCode);
    }
}
