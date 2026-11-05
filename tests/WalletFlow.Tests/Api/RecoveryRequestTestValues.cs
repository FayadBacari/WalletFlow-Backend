using System.Net.Http.Json;
using System.Text.Json;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Api;

// Valeurs et outils communs aux tests de la récupération et du déblocage.
public static class RecoveryRequestTestValues
{
    public const string AddressOfTheForgotPasswordEndpoint = "/api/account/forgot-password";
    public const string AddressOfTheResetPasswordEndpoint = "/api/account/reset-password";
    public const string AddressOfTheAccountUnlockEndpoint = "/api/account/unlock";
    public const string NewPasswordChosenInTheRecoveryTests = "Nouveau-Mot-De-Passe-2";

    // Attend qu'une condition devienne vraie : les e-mails sont préparés en arrière-plan, après la réponse de la route.
    public static async Task WaitUntilThisConditionBecomesTrueAsync(Func<bool> conditionToWaitForInThisTest)
    {
        for (var numberOfTheCheckBeingDone = 1; numberOfTheCheckBeingDone <= 200; numberOfTheCheckBeingDone++)
        {
            if (conditionToWaitForInThisTest())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        Assert.True(conditionToWaitForInThisTest(), "La condition attendue n'est jamais devenue vraie.");
    }

    // Envoie une demande de récupération.
    public static async Task<HttpResponseMessage> SendForgotPasswordRequestToTheApiAsync(HttpClient clientThatCallsTheApiInMemory, string emailAddressTypedInThisTest)
    {
        return await clientThatCallsTheApiInMemory.PostAsJsonAsync(AddressOfTheForgotPasswordEndpoint, new { email = emailAddressTypedInThisTest });
    }

    // Envoie un nouveau mot de passe avec le jeton du lien.
    public static async Task<HttpResponseMessage> SendResetPasswordRequestToTheApiAsync(
        HttpClient clientThatCallsTheApiInMemory,
        string tokenReadInTheRecoveryLink,
        string newPasswordTypedInThisTest = NewPasswordChosenInTheRecoveryTests)
    {
        return await clientThatCallsTheApiInMemory.PostAsJsonAsync(AddressOfTheResetPasswordEndpoint, new { token = tokenReadInTheRecoveryLink, newPassword = newPasswordTypedInThisTest });
    }

    // Envoie le jeton du lien de déblocage.
    public static async Task<HttpResponseMessage> SendAccountUnlockRequestToTheApiAsync(HttpClient clientThatCallsTheApiInMemory, string tokenReadInTheUnlockLink)
    {
        return await clientThatCallsTheApiInMemory.PostAsJsonAsync(AddressOfTheAccountUnlockEndpoint, new { token = tokenReadInTheUnlockLink });
    }

    // Demande un lien de récupération, attend l'e-mail, et renvoie le jeton de son lien.
    public static async Task<string> RequestRecoveryThenReadTheTokenOfTheLinkAsync(
        ApiFactoryWithFakeRepositoryAndEmails apiStartedInMemoryForThisTest,
        HttpClient clientThatCallsTheApiInMemory,
        string emailAddressTypedInThisTest)
    {
        var numberOfEmailsBeforeTheRequest = apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails.Count;

        await SendForgotPasswordRequestToTheApiAsync(clientThatCallsTheApiInMemory, emailAddressTypedInThisTest);
        await WaitUntilThisConditionBecomesTrueAsync(() => apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails.Count > numberOfEmailsBeforeTheRequest);

        return ReadTokenWrittenInTheConfirmationLink(apiStartedInMemoryForThisTest.SenderThatKeepsTheEmailsInMemory.SentEmails.Last().Body);
    }

    // Lit le premier message d'erreur d'un champ dans la réponse.
    public static async Task<string?> ReadErrorMessageOfThisRequestFieldAsync(HttpResponseMessage answerOfTheApiToRead, string nameOfTheFieldWithTheError)
    {
        var jsonDocumentOfTheErrorAnswer = JsonDocument.Parse(await answerOfTheApiToRead.Content.ReadAsStringAsync());

        return jsonDocumentOfTheErrorAnswer.RootElement.GetProperty("errors").GetProperty(nameOfTheFieldWithTheError)[0].GetString();
    }
}
