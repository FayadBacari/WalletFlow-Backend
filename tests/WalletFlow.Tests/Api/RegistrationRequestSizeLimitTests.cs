using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Api.Configuration;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la limite de taille des requêtes avec le vrai serveur Kestrel : le serveur de test ne l'applique pas.
public class RegistrationRequestSizeLimitTests
{
    // Le système choisit un port libre : un port fixe ferait échouer ces tests dès qu'un autre programme l'utilise.
    private const int AnyFreePort = 0;

    // Une requête trop grosse reçoit 413, sans détail technique, et n'arrive jamais à la base.
    [Fact]
    public async Task RequestBiggerThanTheLimitIsRefusedWithPayloadTooLarge()
    {
        using var api = new ApiFactoryInProduction();
        api.UseKestrel(AnyFreePort);
        var client = api.CreateClient();
        var tooBigBody = "{\"email\":\"" + new string('a', 5000) + "@exemple.fr\",\"password\":\"" + ValidPassword + "\"}";

        var response = await client.PostAsync(
            RegistrationEndpoint,
            new StringContent(tooBigBody, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var responseText = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Request body too large", responseText);
        Assert.DoesNotContain("4096", responseText);
        Assert.Empty(api.AccountRepository.SavedUsers);
        SecurityHeadersAssertions.AssertExactSecurityHeaders(response);
    }

    // La plus longue adresse et le plus long mot de passe permis passent, même si chaque emoji est écrit en \uXXXX.
    [Fact]
    public async Task BiggestValidRequestStaysUnderTheLimit()
    {
        using var api = new ApiFactoryInProduction();
        api.UseKestrel(AnyFreePort);
        var client = api.CreateClient();
        var longestEmailAddress = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 58) + ".fr";
        var longestValidPassword = "Aa1!" + string.Concat(Enumerable.Repeat("\\ud83d\\ude00", 124));
        var biggestValidBody = "{\"email\":\"" + longestEmailAddress + "\",\"password\":\"" + longestValidPassword + "\"}";

        var response = await client.PostAsync(
            RegistrationEndpoint,
            new StringContent(biggestValidBody, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(api.AccountRepository.SavedUsers);
    }

    // La route de confirmation a la même limite : le mot de passe saisi n'arrive jamais au hachage s'il est géant.
    [Fact]
    public async Task ConfirmationRequestBiggerThanTheLimitIsRefusedWithPayloadTooLarge()
    {
        using var api = new ApiFactoryInProduction();
        api.UseKestrel(AnyFreePort);
        var client = api.CreateClient();
        var tooBigBody = "{\"token\":\"jeton\",\"password\":\"" + new string('a', 5000) + "\"}";

        var response = await client.PostAsync(
            ConfirmationEndpoint,
            new StringContent(tooBigBody, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    // La route de connexion a la même limite : le mot de passe saisi n'arrive jamais au hachage s'il est géant.
    [Fact]
    public async Task LoginRequestBiggerThanTheLimitIsRefusedWithPayloadTooLarge()
    {
        using var api = new ApiFactoryInProduction();
        api.UseKestrel(AnyFreePort);
        var client = api.CreateClient();
        var tooBigBody = "{\"email\":\"utilisateur@exemple.test\",\"password\":\"" + new string('a', 5000) + "\"}";

        var response = await client.PostAsync(
            LoginRequestTestValues.LoginEndpoint,
            new StringContent(tooBigBody, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(api.SecurityEventRepository.SavedSecurityEvents);
    }

    // Le plus long mot de passe permis passe sur la route de connexion.
    [Fact]
    public async Task BiggestValidLoginRequestStaysUnderTheLimit()
    {
        using var api = new ApiFactoryInProduction();
        api.UseKestrel(AnyFreePort);
        var client = api.CreateClient();
        var longestEmailAddress = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 58) + ".fr";
        var longestValidPassword = "Aa1!" + string.Concat(Enumerable.Repeat("\\ud83d\\ude00", 124));
        var biggestValidBody = "{\"email\":\"" + longestEmailAddress + "\",\"password\":\"" + longestValidPassword + "\"}";

        var response = await client.PostAsync(
            LoginRequestTestValues.LoginEndpoint,
            new StringContent(biggestValidBody, Encoding.UTF8, "application/json"));

        // Le compte n'existe pas : la requête a donc bien été lue jusqu'au bout, sans erreur de taille.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Les routes de récupération et de déblocage ont la même limite.
    [Theory]
    [InlineData(RecoveryRequestTestValues.ForgotPasswordEndpoint, "email")]
    [InlineData(RecoveryRequestTestValues.ResetPasswordEndpoint, "newPassword")]
    [InlineData(RecoveryRequestTestValues.UnlockEndpoint, "token")]
    public async Task RecoveryRequestBiggerThanTheLimitIsRefusedWithPayloadTooLarge(string address, string longFieldName)
    {
        using var api = new ApiFactoryInProduction();
        api.UseKestrel(AnyFreePort);
        var client = api.CreateClient();
        var tooBigBody = "{\"token\":\"jeton\",\"" + longFieldName + "\":\"" + new string('a', 5000) + "\"}";

        var response = await client.PostAsync(
            address,
            new StringContent(tooBigBody, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    // La plus longue adresse permise passe sur la demande de récupération.
    [Fact]
    public async Task BiggestValidForgotPasswordRequestStaysUnderTheLimit()
    {
        using var api = new ApiFactoryInProduction();
        api.UseKestrel(AnyFreePort);
        var client = api.CreateClient();
        var longestEmailAddress = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 58) + ".fr";

        var response = await client.PostAsync(
            RecoveryRequestTestValues.ForgotPasswordEndpoint,
            new StringContent("{\"email\":\"" + longestEmailAddress + "\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Le plus long jeton réel et le plus long mot de passe permis passent sur la route du nouveau mot de passe.
    [Fact]
    public async Task BiggestValidResetPasswordRequestStaysUnderTheLimit()
    {
        using var api = new ApiFactoryInProduction();
        api.UseKestrel(AnyFreePort);
        var client = api.CreateClient();
        var longestValidPassword = "Aa1!" + string.Concat(Enumerable.Repeat("\\ud83d\\ude00", 124));
        var biggestValidBody = "{\"token\":\"" + new string('a', 43) + "\",\"newPassword\":\"" + longestValidPassword + "\"}";

        var response = await client.PostAsync(
            RecoveryRequestTestValues.ResetPasswordEndpoint,
            new StringContent(biggestValidBody, Encoding.UTF8, "application/json"));

        // Le jeton est inconnu : la requête a donc bien été lue jusqu'au bout, sans erreur de taille.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Le plus long jeton réel et le plus long mot de passe permis passent sur la route de confirmation.
    [Fact]
    public async Task BiggestValidConfirmationRequestStaysUnderTheLimit()
    {
        using var api = new ApiFactoryInProduction();
        api.UseKestrel(AnyFreePort);
        var client = api.CreateClient();
        var longestValidPassword = "Aa1!" + string.Concat(Enumerable.Repeat("\\ud83d\\ude00", 124));
        var biggestValidBody = "{\"token\":\"" + new string('a', 43) + "\",\"password\":\"" + longestValidPassword + "\"}";

        var response = await client.PostAsync(
            ConfirmationEndpoint,
            new StringContent(biggestValidBody, Encoding.UTF8, "application/json"));

        // Le jeton est inconnu : la requête a donc bien été lue jusqu'au bout, sans erreur de taille.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // La route du code de double authentification a la même limite, et une requête trop grosse n'écrit rien au journal.
    [Fact]
    public async Task TwoFactorCodeRequestBiggerThanTheLimitIsRefusedWithPayloadTooLarge()
    {
        using var api = new ApiFactoryInProduction();
        api.UseKestrel(AnyFreePort);
        var client = api.CreateClient();
        var tooBigBody = "{\"code\":\"" + new string('1', 5000) + "\"}";

        var response = await client.PostAsync(
            TwoFactorRequestTestValues.TwoFactorLoginEndpoint,
            new StringContent(tooBigBody, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(api.SecurityEventRepository.SavedSecurityEvents);
    }

    // Sans session, ces routes répondent 401 avant de lire la requête, et les cookies Secure ne partent pas en HTTP vers Kestrel :
    // ce test lit donc la limite déclarée sur chacune, et le parcours réel en HTTPS vérifie la réponse 413.
    [Theory]
    [InlineData("api/account/two-factor/start-activation")]
    [InlineData("api/account/two-factor/finish-activation")]
    [InlineData("api/account/two-factor/deactivate")]
    public void TwoFactorSettingsRoutesDeclareTheSizeLimit(string template)
    {
        using var api = new ApiFactoryWithFakes();
        var routeProvider = api.Services.GetRequiredService<IActionDescriptorCollectionProvider>();

        var route = routeProvider.ActionDescriptors.Items.Single(route => route.AttributeRouteInfo!.Template == template);

        Assert.Equal(RequestSizeConfiguration.MaximumAccountRequestBytes, route.EndpointMetadata.OfType<IRequestSizeLimitMetadata>().Single().MaxRequestBodySize);
    }
}
