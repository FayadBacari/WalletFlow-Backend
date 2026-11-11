using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;
using WalletFlow.Api.Configuration;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Valeurs et outils communs aux tests de la connexion et des sessions.
public static class LoginRequestTestValues
{
    public const string LoginEndpoint = "/api/account/login";
    public const string CsrfTokenEndpoint = "/api/account/csrf-token";
    public const string LogoutEndpoint = "/api/account/logout";
    public const string Email = "utilisateur@exemple.test";
    public const string WrongPassword = "Mauvais-Mot-De-Passe-2";

    // Crée un client qui garde les cookies comme un navigateur. En HTTPS : un cookie Secure n'est jamais renvoyé en HTTP.
    public static HttpClient CreateBrowserClient(ApiFactoryWithFakes api)
    {
        return api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
    }

    // Crée un client qui n'envoie que les cookies écrits à la main par le test, comme un attaquant qui les aurait copiés.
    public static HttpClient CreateClientWithManualCookies(ApiFactoryWithFakes api, string cookies)
    {
        var client = api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });
        client.DefaultRequestHeaders.Add("Cookie", cookies);

        return client;
    }

    // Ajoute à la fausse base un compte avec cette adresse et le mot de passe des tests, haché avec le vrai Argon2.
    public static async Task<User> AddAccountAsync(
        ApiFactoryWithFakes api,
        string emailAddress = Email,
        bool isEmailConfirmed = true)
    {
        var account = new User
        {
            Email = emailAddress,
            PasswordHash = await RealArgonHashers.CreatePasswordHasher().HashAsync(ValidPassword),
            IsEmailConfirmed = isEmailConfirmed
        };
        api.AccountRepository.SavedUsers.Add(account);

        return account;
    }

    // Ajoute à la fausse base un compte bloqué jusqu'à cette date, avec ce nombre de blocages déjà comptés.
    public static async Task<User> AddLockedAccountAsync(
        ApiFactoryWithFakes api,
        string emailAddress,
        short lockoutCount,
        DateTime lockoutEndsAt)
    {
        var account = new User
        {
            Email = emailAddress,
            PasswordHash = await RealArgonHashers.CreatePasswordHasher().HashAsync(ValidPassword),
            IsEmailConfirmed = true,
            FailedLoginAttemptCount = AccountLockoutRules.MaximumFailedAttempts,
            LockoutCount = lockoutCount,
            LockoutEndsAt = lockoutEndsAt
        };
        api.AccountRepository.SavedUsers.Add(account);

        return account;
    }

    // Envoie une demande de connexion.
    public static async Task<HttpResponseMessage> SendLoginRequestAsync(
        HttpClient client,
        string emailAddress = Email,
        string password = ValidPassword)
    {
        return await client.PostAsJsonAsync(
            LoginEndpoint,
            new { email = emailAddress, password = password });
    }

    // Demande le jeton anti-CSRF de l'utilisateur connecté.
    public static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync(CsrfTokenEndpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return jsonDocument.RootElement.GetProperty("csrfToken").GetString()!;
    }

    // Envoie une demande de déconnexion, avec ce jeton anti-CSRF si le test en donne un.
    public static async Task<HttpResponseMessage> SendLogoutRequestAsync(HttpClient client, string? csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, LogoutEndpoint);

        if (csrfToken != null)
        {
            request.Headers.Add(AntiforgeryConfiguration.CsrfHeaderName, csrfToken);
        }

        return await client.SendAsync(request);
    }

    // Retrouve un cookie parmi ceux que la réponse demande d'écrire. Renvoie null si elle n'en parle pas.
    public static SetCookieHeaderValue? FindCookie(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        foreach (var header in cookies)
        {
            var cookie = SetCookieHeaderValue.Parse(header);

            if (cookie.Name == cookieName)
            {
                return cookie;
            }
        }

        return null;
    }

    // Vérifie qu'une erreur a le format commun, avec exactement ce titre en français.
    public static async Task AssertProblemTitleAsync(HttpResponseMessage response, HttpStatusCode statusCode, string title)
    {
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(title, jsonDocument.RootElement.GetProperty("title").GetString());
    }
}
