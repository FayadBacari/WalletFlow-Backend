using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.EndToEnd;

// Étapes communes des parcours sur la vraie API : un navigateur, puis un compte inscrit et confirmé comme un vrai utilisateur.
public static class RealAccountSteps
{
    // Crée un client qui garde les cookies comme un navigateur, en HTTPS.
    public static HttpClient CreateBrowser(ApiFactoryWithRealDatabase api)
    {
        return api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
    }

    // Inscrit puis confirme un compte avec cette adresse et le mot de passe des tests.
    public static async Task RegisterThenConfirmAsync(ApiFactoryWithRealDatabase api, HttpClient client, string email)
    {
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(RegistrationEndpoint, new { email = email, password = ValidPassword })).StatusCode);

        var confirmationEmail = api.EmailSender.SentEmails.Single(sentEmail => sentEmail.RecipientAddress == email);
        var confirmationResponse = await client.PostAsJsonAsync(
            ConfirmationEndpoint,
            new { token = ReadConfirmationToken(confirmationEmail.Body), password = ValidPassword });
        Assert.Equal(HttpStatusCode.OK, confirmationResponse.StatusCode);
    }
}
