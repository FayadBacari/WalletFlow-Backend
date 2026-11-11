using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using WalletFlow.Api.Filters;
using static WalletFlow.Tests.Api.LoginRequestTestValues;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie que chaque réponse de la connexion et de l'inscription dure au moins la durée minimale : son temps ne révèle aucun compte (SS §2).
public class MinimumAnswerDurationTests
{
    // Un minuteur peut finir une fraction de milliseconde avant l'heure du chronomètre : cette marge n'aide aucune attaque.
    private static readonly TimeSpan MinimumAcceptedDuration =
        TimeSpan.FromMilliseconds(ApiFactoryWithMinimumAnswerDuration.MinimumMilliseconds - 5);

    // Une réponse, et le temps qu'elle a pris.
    private class TimedResponse
    {
        public required HttpStatusCode StatusCode { get; init; }
        public required TimeSpan Duration { get; init; }
    }

    // Envoie une requête, et mesure le temps de sa réponse.
    private static async Task<TimedResponse> MeasureAsync(Func<Task<HttpResponseMessage>> sendRequest)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await sendRequest();
        stopwatch.Stop();

        return new TimedResponse
        {
            StatusCode = response.StatusCode,
            Duration = stopwatch.Elapsed
        };
    }

    // Envoie une inscription avec un mot de passe valable.
    private static async Task<HttpResponseMessage> SendRegistrationRequestAsync(HttpClient client, string emailAddress)
    {
        return await client.PostAsJsonAsync(
            RegistrationEndpoint,
            new { email = emailAddress, password = ValidPassword });
    }

    [Fact]
    public async Task UnknownAddressConfirmedAccountAndUnconfirmedAccountAllWaitTheMinimumDuration()
    {
        using var api = new ApiFactoryWithMinimumAnswerDuration();
        await AddAccountAsync(api, "compte-confirme@exemple.test");
        await AddAccountAsync(api, "compte-non-confirme@exemple.test", isEmailConfirmed: false);
        var client = api.CreateClient();

        var unknownAddressResponse = await MeasureAsync(() => SendLoginRequestAsync(client, "inconnu@exemple.test", ValidPassword));
        var confirmedResponse = await MeasureAsync(() => SendLoginRequestAsync(client, "compte-confirme@exemple.test", WrongPassword));
        var unconfirmedResponse = await MeasureAsync(() => SendLoginRequestAsync(client, "compte-non-confirme@exemple.test", WrongPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, unknownAddressResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, confirmedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unconfirmedResponse.StatusCode);
        Assert.True(unknownAddressResponse.Duration >= MinimumAcceptedDuration);
        Assert.True(confirmedResponse.Duration >= MinimumAcceptedDuration);
        Assert.True(unconfirmedResponse.Duration >= MinimumAcceptedDuration);
    }

    // Une connexion réussie attend aussi : sinon, la réussite se verrait au temps de réponse avant même la réponse.
    [Fact]
    public async Task SuccessfulLoginAlsoWaitsTheMinimumDuration()
    {
        using var api = new ApiFactoryWithMinimumAnswerDuration();
        await AddAccountAsync(api);
        var client = api.CreateClient();

        var response = await MeasureAsync(() => SendLoginRequestAsync(client, Email, ValidPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Duration >= MinimumAcceptedDuration);
    }

    // L'inscription d'une adresse libre écrit un compte, celle d'une adresse confirmée n'écrit rien :
    // les deux réponses attendent la même durée minimale, et leur temps ne dit pas laquelle est déjà utilisée.
    [Fact]
    public async Task RegistrationOfAFreeAddressAndOfAConfirmedAddressBothWaitTheMinimumDuration()
    {
        using var api = new ApiFactoryWithMinimumAnswerDuration();
        await AddAccountAsync(api, "compte-confirme@exemple.test");
        var client = api.CreateClient();

        var freeAddressResponse = await MeasureAsync(() => SendRegistrationRequestAsync(client, "adresse-libre@exemple.test"));
        var confirmedAddressResponse = await MeasureAsync(() => SendRegistrationRequestAsync(client, "compte-confirme@exemple.test"));

        Assert.Equal(HttpStatusCode.OK, freeAddressResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, confirmedAddressResponse.StatusCode);
        Assert.True(freeAddressResponse.Duration >= MinimumAcceptedDuration);
        Assert.True(confirmedAddressResponse.Duration >= MinimumAcceptedDuration);
    }

    [Theory]
    [InlineData(null, 500)]
    [InlineData("", 500)]
    [InlineData("0", 0)]
    [InlineData("250", 250)]
    public void SettingGivesTheMinimumDurationAndItsAbsenceGivesTheDefaultOne(string? settingText, int expectedMilliseconds)
    {
        var configuration = CreateConfigurationWith(settingText);

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), MinimumAnswerDurationFilter.ReadMinimumDuration(configuration));
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("cinq-cents")]
    [InlineData("1.5")]
    public void UnreadableOrNegativeSettingPreventsTheApiFromStarting(string settingText)
    {
        var configuration = CreateConfigurationWith(settingText);

        Assert.Throws<InvalidOperationException>(() => MinimumAnswerDurationFilter.ReadMinimumDuration(configuration));
    }

    // Crée une configuration qui ne contient que ce réglage, ou rien.
    private static IConfiguration CreateConfigurationWith(string? settingText)
    {
        var settings = new Dictionary<string, string?>();

        if (settingText != null)
        {
            settings.Add(MinimumAnswerDurationFilter.SettingName, settingText);
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }
}
