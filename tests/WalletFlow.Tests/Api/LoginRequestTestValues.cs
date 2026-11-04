using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;
using WalletFlow.Api.Configuration;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Security;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Valeurs et outils communs aux tests de la connexion et des sessions.
public static class LoginRequestTestValues
{
    public const string AddressOfTheAccountLoginEndpoint = "/api/account/login";
    public const string AddressOfTheCsrfTokenEndpoint = "/api/account/csrf-token";
    public const string AddressOfTheUserLogoutEndpoint = "/api/account/logout";
    public const string EmailAddressOfTheLoginTests = "utilisateur@exemple.test";
    public const string WrongPasswordTypedInTheLoginTests = "Mauvais-Mot-De-Passe-2";

    // Crée un client qui garde les cookies comme un navigateur. En HTTPS : un cookie Secure n'est jamais renvoyé en HTTP.
    public static HttpClient CreateClientThatKeepsCookiesLikeABrowser(ApiFactoryWithFakeRepositoryAndEmails apiStartedInMemoryForThisTest)
    {
        return apiStartedInMemoryForThisTest.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
    }

    // Crée un client qui n'envoie que les cookies écrits à la main par le test, comme un attaquant qui les aurait copiés.
    public static HttpClient CreateClientThatOnlySendsHandwrittenCookies(ApiFactoryWithFakeRepositoryAndEmails apiStartedInMemoryForThisTest, string cookiesWrittenByHandInTheTest)
    {
        var clientThatOnlySendsHandwrittenCookies = apiStartedInMemoryForThisTest.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });
        clientThatOnlySendsHandwrittenCookies.DefaultRequestHeaders.Add("Cookie", cookiesWrittenByHandInTheTest);

        return clientThatOnlySendsHandwrittenCookies;
    }

    // Ajoute à la fausse base un compte avec cette adresse et le mot de passe des tests, haché avec le vrai Argon2.
    public static User AddAccountToTheFakeRepositoryOfTheApi(
        ApiFactoryWithFakeRepositoryAndEmails apiStartedInMemoryForThisTest,
        string emailAddressOfTheAccountToAdd = EmailAddressOfTheLoginTests,
        bool emailAddressOfTheAccountIsConfirmed = true)
    {
        var accountAddedToTheFakeRepository = new User
        {
            Email = emailAddressOfTheAccountToAdd,
            PasswordHash = new LibsodiumPasswordHasher().ConvertPasswordIntoItsSecureHash(ValidPasswordChosenInTheseTests),
            IsEmailConfirmed = emailAddressOfTheAccountIsConfirmed
        };
        apiStartedInMemoryForThisTest.RepositoryThatKeepsAccountsInMemory.SavedUsers.Add(accountAddedToTheFakeRepository);

        return accountAddedToTheFakeRepository;
    }

    // Envoie une demande de connexion.
    public static async Task<HttpResponseMessage> SendLoginRequestToTheApiAsync(
        HttpClient clientThatCallsTheApiInMemory,
        string emailAddressTypedInThisTest = EmailAddressOfTheLoginTests,
        string passwordTypedByTheVisitorInThisTest = ValidPasswordChosenInTheseTests)
    {
        return await clientThatCallsTheApiInMemory.PostAsJsonAsync(
            AddressOfTheAccountLoginEndpoint,
            new { email = emailAddressTypedInThisTest, password = passwordTypedByTheVisitorInThisTest });
    }

    // Demande le jeton anti-CSRF de l'utilisateur connecté.
    public static async Task<string> AskForTheCsrfTokenOfTheLoggedInUserAsync(HttpClient clientThatCallsTheApiInMemory)
    {
        var answerToTheCsrfTokenRequest = await clientThatCallsTheApiInMemory.GetAsync(AddressOfTheCsrfTokenEndpoint);
        Assert.Equal(HttpStatusCode.OK, answerToTheCsrfTokenRequest.StatusCode);

        var jsonDocumentOfTheApiAnswer = JsonDocument.Parse(await answerToTheCsrfTokenRequest.Content.ReadAsStringAsync());

        return jsonDocumentOfTheApiAnswer.RootElement.GetProperty("csrfToken").GetString()!;
    }

    // Envoie une demande de déconnexion, avec ce jeton anti-CSRF si le test en donne un.
    public static async Task<HttpResponseMessage> SendLogoutRequestWithThisCsrfTokenAsync(HttpClient clientThatCallsTheApiInMemory, string? csrfTokenSentWithTheRequest)
    {
        var logoutRequestToSendToTheApi = new HttpRequestMessage(HttpMethod.Post, AddressOfTheUserLogoutEndpoint);

        if (csrfTokenSentWithTheRequest != null)
        {
            logoutRequestToSendToTheApi.Headers.Add(AntiforgeryConfiguration.NameOfTheHeaderCarryingTheCsrfToken, csrfTokenSentWithTheRequest);
        }

        return await clientThatCallsTheApiInMemory.SendAsync(logoutRequestToSendToTheApi);
    }

    // Retrouve un cookie parmi ceux que la réponse demande d'écrire. Renvoie null si elle n'en parle pas.
    public static SetCookieHeaderValue? FindCookieWrittenByTheAnswer(HttpResponseMessage answerOfTheApiToRead, string nameOfTheCookieToFind)
    {
        if (!answerOfTheApiToRead.Headers.TryGetValues("Set-Cookie", out var allCookiesWrittenByTheAnswer))
        {
            return null;
        }

        foreach (var oneCookieWrittenByTheAnswer in allCookiesWrittenByTheAnswer)
        {
            var cookieReadFromTheAnswerHeader = SetCookieHeaderValue.Parse(oneCookieWrittenByTheAnswer);

            if (cookieReadFromTheAnswerHeader.Name == nameOfTheCookieToFind)
            {
                return cookieReadFromTheAnswerHeader;
            }
        }

        return null;
    }

    // Vérifie qu'une erreur a le format commun, avec exactement ce titre en français.
    public static async Task CheckThatAnswerIsProblemWithThisExactTitleAsync(HttpResponseMessage answerOfTheApiToCheck, HttpStatusCode statusCodeExpectedInTheAnswer, string titleExpectedInTheErrorAnswer)
    {
        Assert.Equal(statusCodeExpectedInTheAnswer, answerOfTheApiToCheck.StatusCode);
        Assert.Equal("application/problem+json", answerOfTheApiToCheck.Content.Headers.ContentType?.MediaType);

        var jsonDocumentOfTheApiAnswer = JsonDocument.Parse(await answerOfTheApiToCheck.Content.ReadAsStringAsync());
        Assert.Equal(titleExpectedInTheErrorAnswer, jsonDocumentOfTheApiAnswer.RootElement.GetProperty("title").GetString());
    }
}
