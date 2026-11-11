using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using WalletFlow.Api.Configuration;

namespace WalletFlow.Tests.Api;

// Vérifie la règle des clés d'erreur : seul le nom d'un champ est gardé, jamais un texte qui pourrait venir du visiteur (SS §10).
public class ApiErrorConfigurationTests
{
    private const string ValidationMessage = "message-ecrit-par-les-regles";

    // Construit la réponse d'une requête refusée avec une erreur pour chacune de ces clés.
    private static ValidationProblemDetails CreateAnswerForErrorKeys(params string[] keys)
    {
        using var productionApi = new ApiFactoryInProduction();
        var errors = new ModelStateDictionary();

        foreach (var errorKey in keys)
        {
            errors.AddModelError(errorKey, ValidationMessage);
        }

        var context = new ActionContext(
            new DefaultHttpContext { RequestServices = productionApi.Services },
            new RouteData(),
            new ActionDescriptor(),
            errors);

        var response = Assert.IsType<BadRequestObjectResult>(ApiErrorConfiguration.CreateRefusedRequestAnswer(context));
        return Assert.IsType<ValidationProblemDetails>(response.Value);
    }

    // Une erreur sur un champ garde sa clé stable et son message.
    [Theory]
    [InlineData("Email")]
    [InlineData("Password")]
    [InlineData("Token2")]
    public void NameOfRequestFieldKeepsItsKeyAndItsMessage(string fieldName)
    {
        var problemDetails = CreateAnswerForErrorKeys(fieldName);

        Assert.Equal(ValidationMessage, Assert.Single(problemDetails.Errors[fieldName]));
    }

    // Toute autre clé pourrait citer un nom interne du code ou recopier un texte du visiteur : ni elle ni son message ne sortent.
    [Theory]
    [InlineData("")]
    [InlineData("$")]
    [InlineData("$.email")]
    [InlineData("$['<script>alert(1)</script>']")]
    [InlineData("request")]
    [InlineData("Email<script>")]
    [InlineData("Email.Name")]
    [InlineData("Émail")]
    public void AnyOtherKeyBecomesTheGenericErrorWithoutItsMessage(string unknownKey)
    {
        var problemDetails = CreateAnswerForErrorKeys(unknownKey);

        var onlyError = Assert.Single(problemDetails.Errors);
        Assert.Equal(ApiErrorConfiguration.GeneralErrorsKey, onlyError.Key);
        Assert.Equal(ApiErrorConfiguration.UnreadableRequestMessage, Assert.Single(onlyError.Value));
    }

    // Plusieurs erreurs illisibles dans la même requête ne donnent qu'un seul message générique.
    [Fact]
    public void SeveralUnreadableErrorsGiveOnlyOneGenericMessage()
    {
        var problemDetails = CreateAnswerForErrorKeys("$", "$.email", "request");

        var onlyError = Assert.Single(problemDetails.Errors);
        Assert.Single(onlyError.Value);
    }
}
