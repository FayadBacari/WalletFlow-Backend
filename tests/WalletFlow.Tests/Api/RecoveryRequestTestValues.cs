using System.Net.Http.Json;
using System.Text.Json;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Valeurs et outils communs aux tests de la récupération et du déblocage.
public static class RecoveryRequestTestValues
{
    public const string ForgotPasswordEndpoint = "/api/account/forgot-password";
    public const string ResetPasswordEndpoint = "/api/account/reset-password";
    public const string UnlockEndpoint = "/api/account/unlock";
    public const string NewPassword = "Nouveau-Mot-De-Passe-2";

    // Attend qu'une condition devienne vraie : les e-mails sont préparés en arrière-plan, après la réponse de la route.
    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var checkNumber = 1; checkNumber <= 200; checkNumber++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        Assert.True(condition(), "La condition attendue n'est jamais devenue vraie.");
    }

    // Envoie une demande de récupération.
    public static async Task<HttpResponseMessage> SendForgotPasswordRequestAsync(HttpClient client, string emailAddress)
    {
        return await client.PostAsJsonAsync(ForgotPasswordEndpoint, new { email = emailAddress });
    }

    // Envoie un nouveau mot de passe avec le jeton du lien.
    public static async Task<HttpResponseMessage> SendResetPasswordRequestAsync(
        HttpClient client,
        string linkToken,
        string newPassword = NewPassword)
    {
        return await client.PostAsJsonAsync(ResetPasswordEndpoint, new { token = linkToken, newPassword = newPassword });
    }

    // Envoie le jeton du lien de déblocage.
    public static async Task<HttpResponseMessage> SendUnlockRequestAsync(HttpClient client, string linkToken)
    {
        return await client.PostAsJsonAsync(UnlockEndpoint, new { token = linkToken });
    }

    // Demande un lien de récupération, attend l'e-mail, et renvoie le jeton de son lien.
    public static async Task<string> RequestRecoveryThenReadTokenAsync(
        ApiFactoryWithFakes api,
        HttpClient client,
        string emailAddress)
    {
        var emailCount = api.EmailSender.SentEmails.Count;

        await SendForgotPasswordRequestAsync(client, emailAddress);
        await WaitUntilAsync(() => api.EmailSender.SentEmails.Count > emailCount);

        return ReadConfirmationToken(api.EmailSender.SentEmails.Last().Body);
    }

    // Lit le premier message d'erreur d'un champ dans la réponse.
    public static async Task<string?> ReadFieldErrorAsync(HttpResponseMessage response, string fieldName)
    {
        var jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return jsonDocument.RootElement.GetProperty("errors").GetProperty(fieldName)[0].GetString();
    }
}
