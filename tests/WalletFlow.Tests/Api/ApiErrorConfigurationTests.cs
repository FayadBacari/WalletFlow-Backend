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
    private const string MessageWrittenByTheValidationRules = "message-ecrit-par-les-regles";

    // Construit la réponse d'une requête refusée avec une erreur pour chacune de ces clés.
    private static ValidationProblemDetails CreateAnswerForTheseKeysOfErrors(params string[] keysOfTheErrorsToAnswer)
    {
        using var apiStartedLikeInProductionForThisTest = new ApiFactoryRunningLikeInProduction();
        var errorsOfTheRequestToRefuse = new ModelStateDictionary();

        foreach (var keyOfTheErrorToAddToTheRequest in keysOfTheErrorsToAnswer)
        {
            errorsOfTheRequestToRefuse.AddModelError(keyOfTheErrorToAddToTheRequest, MessageWrittenByTheValidationRules);
        }

        var contextOfTheRequestToRefuse = new ActionContext(
            new DefaultHttpContext { RequestServices = apiStartedLikeInProductionForThisTest.Services },
            new RouteData(),
            new ActionDescriptor(),
            errorsOfTheRequestToRefuse);

        var answerToTheRequestToRefuse = Assert.IsType<BadRequestObjectResult>(ApiErrorConfiguration.CreateAnswerForTheRefusedRequest(contextOfTheRequestToRefuse));
        return Assert.IsType<ValidationProblemDetails>(answerToTheRequestToRefuse.Value);
    }

    // Une erreur sur un champ garde sa clé stable et son message.
    [Theory]
    [InlineData("Email")]
    [InlineData("Password")]
    [InlineData("Token2")]
    public void NameOfRequestFieldKeepsItsKeyAndItsMessage(string nameOfTheFieldWithTheError)
    {
        var detailsOfTheRefusedRequestAnswer = CreateAnswerForTheseKeysOfErrors(nameOfTheFieldWithTheError);

        Assert.Equal(MessageWrittenByTheValidationRules, Assert.Single(detailsOfTheRefusedRequestAnswer.Errors[nameOfTheFieldWithTheError]));
    }

    // Toute autre clé pourrait citer un nom interne du code ou recopier un texte du visiteur : ni elle ni son message ne sortent.
    [Theory]
    [InlineData("")]
    [InlineData("$")]
    [InlineData("$.email")]
    [InlineData("$['<script>alert(1)</script>']")]
    [InlineData("requestSentByTheVisitorToRegister")]
    [InlineData("Email<script>")]
    [InlineData("Email.Name")]
    [InlineData("Émail")]
    public void AnyOtherKeyBecomesTheGenericErrorWithoutItsMessage(string keyThatIsNotTheNameOfRequestField)
    {
        var detailsOfTheRefusedRequestAnswer = CreateAnswerForTheseKeysOfErrors(keyThatIsNotTheNameOfRequestField);

        var onlyErrorKeptInTheAnswer = Assert.Single(detailsOfTheRefusedRequestAnswer.Errors);
        Assert.Equal(ApiErrorConfiguration.KeyOfTheErrorsOutsideTheRequestFields, onlyErrorKeptInTheAnswer.Key);
        Assert.Equal(ApiErrorConfiguration.MessageForRequestThatCannotBeRead, Assert.Single(onlyErrorKeptInTheAnswer.Value));
    }

    // Plusieurs erreurs illisibles dans la même requête ne donnent qu'un seul message générique.
    [Fact]
    public void SeveralUnreadableErrorsGiveOnlyOneGenericMessage()
    {
        var detailsOfTheRefusedRequestAnswer = CreateAnswerForTheseKeysOfErrors("$", "$.email", "requestSentByTheVisitorToRegister");

        var onlyErrorKeptInTheAnswer = Assert.Single(detailsOfTheRefusedRequestAnswer.Errors);
        Assert.Single(onlyErrorKeptInTheAnswer.Value);
    }
}
