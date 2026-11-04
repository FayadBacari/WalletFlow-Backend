using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.UserAccounts;

namespace WalletFlow.Api.Controllers;

// Inscription d'un visiteur (GR Auth). Cette route est appelée sans session : elle n'a donc
// ni besoin d'être connecté, ni besoin du jeton anti-CSRF (SS §6).
[ApiController]
[Route("api/account")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class AccountRegistrationController : ControllerBase
{
    // Le même message pour toute inscription acceptée, que l'adresse soit libre ou déjà utilisée (SS §2).
    public const string MessageSentForEveryAcceptedRegistration =
        "Un e-mail vient d'être envoyé à cette adresse pour finaliser l'inscription.";

    public const string MessageShownWhenTheEmailCouldNotBeSent = "L'e-mail n'a pas pu partir. Réessaie dans quelques minutes.";

    private readonly AccountRegistrationService serviceThatRegistersTheNewUserAccounts;
    private readonly ILogger<AccountRegistrationController> loggerOfTheAccountRegistrationTechnicalProblems;

    // Reçoit le cas d'utilisation de l'inscription et le journal technique.
    public AccountRegistrationController(
        AccountRegistrationService serviceThatRegistersTheNewUserAccounts,
        ILogger<AccountRegistrationController> loggerOfTheAccountRegistrationTechnicalProblems)
    {
        this.serviceThatRegistersTheNewUserAccounts = serviceThatRegistersTheNewUserAccounts;
        this.loggerOfTheAccountRegistrationTechnicalProblems = loggerOfTheAccountRegistrationTechnicalProblems;
    }

    // POST api/account/register : crée un compte et envoie un e-mail de confirmation.
    [HttpPost("register")]
    [EnableRateLimiting(RateLimitingConfiguration.PolicyNameForSignupRequestsPerIpAddress)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumNumberOfBytesInOneAccountRequest)]
    public async Task<IActionResult> RegisterNewAccountFromVisitorRequest(RegisterAccountRequest requestSentByTheVisitorToRegister)
    {
        var resultOfTheVisitorAccountRegistration = await serviceThatRegistersTheNewUserAccounts.RegisterNewAccountForVisitorAsync(
            requestSentByTheVisitorToRegister.Email,
            requestSentByTheVisitorToRegister.Password);

        if (resultOfTheVisitorAccountRegistration.IsAccepted)
        {
            return Ok(new MessageResponse(MessageSentForEveryAcceptedRegistration));
        }

        if (resultOfTheVisitorAccountRegistration.TooManyRequestsForThisEmailAddress)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        if (resultOfTheVisitorAccountRegistration.EmailCouldNotBeSent)
        {
            return CreateAnswerForTheEmailThatCouldNotBeSent(resultOfTheVisitorAccountRegistration);
        }

        return CreateAnswerForTheRequestBreakingTheRules(resultOfTheVisitorAccountRegistration);
    }

    // Note la panne dans le journal, et invite le visiteur à réessayer.
    // Le journal désigne l'utilisateur par son identifiant, jamais par son adresse (SS §7).
    private ObjectResult CreateAnswerForTheEmailThatCouldNotBeSent(AccountRegistrationResult resultOfTheVisitorAccountRegistration)
    {
        loggerOfTheAccountRegistrationTechnicalProblems.LogError(
            "L'e-mail d'inscription n'a pas pu partir pour l'utilisateur {IdentifiantUtilisateur}.",
            resultOfTheVisitorAccountRegistration.UserIdentifierForTheTechnicalLog);

        return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: MessageShownWhenTheEmailCouldNotBeSent);
    }

    // Donne le message de chaque champ refusé.
    private ActionResult CreateAnswerForTheRequestBreakingTheRules(AccountRegistrationResult resultOfTheVisitorAccountRegistration)
    {
        if (resultOfTheVisitorAccountRegistration.EmailAddressErrorMessage != null)
        {
            ModelState.AddModelError(nameof(RegisterAccountRequest.Email), resultOfTheVisitorAccountRegistration.EmailAddressErrorMessage);
        }

        if (resultOfTheVisitorAccountRegistration.PasswordErrorMessage != null)
        {
            ModelState.AddModelError(nameof(RegisterAccountRequest.Password), resultOfTheVisitorAccountRegistration.PasswordErrorMessage);
        }

        return ValidationProblem();
    }
}
