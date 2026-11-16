using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Api.Contracts;
using WalletFlow.Api.Controllers;
using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.Api.TwoFactorRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la route du lien du support qui désactive la double authentification (GR Auth, SS §2 et §6) dans la vraie API, avec une
// fausse base : elle s'ouvre sans session ni jeton anti-CSRF, demande le mot de passe, et répond à chaque refus sur son champ.
public class TwoFactorSupportLinkEndpointTests
{
    private const string LinkEndpoint = "/api/account/disable-two-factor";

    // Ajoute un compte avec la double authentification, et le lien que le support lui a fait envoyer. Renvoie le jeton du lien.
    private static async Task<(User Account, string Token)> AddAccountWithLinkAsync(ApiFactoryWithFakes api)
    {
        var account = (await AddActivatedAccountAsync(api)).UserAccount;
        var generated = api.Services.GetRequiredService<ISecureTokenGenerator>().GenerateToken();
        api.AccountRepository.SavedEmailTokens.Add(new EmailToken
        {
            Type = EmailTokenType.TwoFactorDeactivation,
            TokenHash = generated.Hash,
            ExpiresAt = api.Clock.GetUtcNow().UtcDateTime + EmailTokenValidityDurations.TwoFactorDeactivationLink,
            UserId = account.Id
        });

        return (account, generated.Token);
    }

    // Crée un navigateur sans session, qui écrit depuis cette adresse IP.
    private static HttpClient CreateVisitorClient(ApiFactoryWithFakes api, string ipAddress)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add(VisitorAddressStartupFilter.VisitorAddressHeader, ipAddress);

        return client;
    }

    // Lit le message d'erreur de ce champ dans une réponse 400.
    private static async Task<string?> ReadFieldErrorAsync(HttpResponseMessage response, string fieldName)
    {
        var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");

        return errors.TryGetProperty(fieldName, out var fieldErrors) ? fieldErrors[0].GetString() : null;
    }

    // Sans session ni jeton anti-CSRF, le bon mot de passe désactive la double authentification.
    [Fact]
    public async Task RightPasswordDisablesTwoFactorWithoutSession()
    {
        using var api = new ApiFactoryWithFakes();
        var (account, token) = await AddAccountWithLinkAsync(api);

        var response = await CreateVisitorClient(api, "192.0.2.50").PostAsJsonAsync(LinkEndpoint, new { token = token, password = ValidPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TwoFactorSupportLinkController.TwoFactorDisabledMessage,
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString());
        Assert.False(account.IsTwoFactorEnabled);
    }

    // Un mauvais mot de passe répond sur son champ ; le 5e annule le lien, ce que dit le champ du lien.
    [Fact]
    public async Task WrongPasswordsAnswerOnTheirFieldThenCancelTheLink()
    {
        using var api = new ApiFactoryWithFakes();
        var (account, token) = await AddAccountWithLinkAsync(api);
        var client = CreateVisitorClient(api, "192.0.2.51");

        for (var tryNumber = 1; tryNumber <= 4; tryNumber++)
        {
            var wrong = await client.PostAsJsonAsync(LinkEndpoint, new { token = token, password = WrongPassword });
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            Assert.Equal(ErrorMessages.WrongCurrentPassword, await ReadFieldErrorAsync(wrong, nameof(DisableTwoFactorRequest.Password)));
        }

        var fifth = await client.PostAsJsonAsync(LinkEndpoint, new { token = token, password = WrongPassword });
        var afterwards = await client.PostAsJsonAsync(LinkEndpoint, new { token = token, password = ValidPassword });

        Assert.Equal(ErrorMessages.TwoFactorLinkCancelled, await ReadFieldErrorAsync(fifth, nameof(DisableTwoFactorRequest.Token)));
        Assert.Equal(ErrorMessages.TwoFactorLinkNoLongerValid, await ReadFieldErrorAsync(afterwards, nameof(DisableTwoFactorRequest.Token)));
        Assert.True(account.IsTwoFactorEnabled);
    }

    // Un mot de passe changé il y a moins de 7 jours arrête le lien, ce que dit le champ du lien.
    [Fact]
    public async Task RecentPasswordChangeIsSaidOnTheLinkField()
    {
        using var api = new ApiFactoryWithFakes();
        var (account, token) = await AddAccountWithLinkAsync(api);
        api.SecurityEventRepository.SavedSecurityEvents.Add(new SecurityEvent
        {
            Type = SecurityEventType.PasswordChange,
            Result = SecurityEventResult.Success,
            OccurredAt = api.Clock.GetUtcNow().UtcDateTime.AddDays(-1),
            UserId = account.Id
        });

        var response = await CreateVisitorClient(api, "192.0.2.52").PostAsJsonAsync(LinkEndpoint, new { token = token, password = ValidPassword });

        Assert.Equal(ErrorMessages.TwoFactorLinkAfterPasswordChange, await ReadFieldErrorAsync(response, nameof(DisableTwoFactorRequest.Token)));
        Assert.True(account.IsTwoFactorEnabled);
    }

    // SS §6 : 10 essais par heure et par adresse IP ; le 11e reçoit « trop de requêtes », et une autre adresse a son compteur.
    [Fact]
    public async Task EleventhTryFromTheSameAddressIsRefused()
    {
        using var api = new ApiFactoryWithFakes();
        var client = CreateVisitorClient(api, "192.0.2.53");

        for (var tryNumber = 1; tryNumber <= 10; tryNumber++)
        {
            var response = await client.PostAsJsonAsync(LinkEndpoint, new { token = "lien-inconnu-" + tryNumber, password = ValidPassword });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var eleventh = await client.PostAsJsonAsync(LinkEndpoint, new { token = "lien-inconnu-11", password = ValidPassword });
        var otherAddress = await CreateVisitorClient(api, "192.0.2.54").PostAsJsonAsync(LinkEndpoint, new { token = "lien-inconnu", password = ValidPassword });

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, otherAddress.StatusCode);
    }

    // SS §6 : la route n'accepte que du JSON.
    [Fact]
    public async Task LinkRouteAcceptsOnlyJson()
    {
        using var api = new ApiFactoryWithFakes();
        var (account, token) = await AddAccountWithLinkAsync(api);
        var form = new FormUrlEncodedContent(new Dictionary<string, string> { { "token", token }, { "password", ValidPassword } });

        var response = await CreateVisitorClient(api, "192.0.2.55").PostAsync(LinkEndpoint, form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.True(account.IsTwoFactorEnabled);
    }

    // Un lien ou un mot de passe absent est refusé avant toute lecture.
    [Fact]
    public async Task MissingFieldsAreRefused()
    {
        using var api = new ApiFactoryWithFakes();

        var response = await CreateVisitorClient(api, "192.0.2.56")
            .PostAsync(LinkEndpoint, new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(await ReadFieldErrorAsync(response, nameof(DisableTwoFactorRequest.Token)));
        Assert.NotNull(await ReadFieldErrorAsync(response, nameof(DisableTwoFactorRequest.Password)));
    }
}
