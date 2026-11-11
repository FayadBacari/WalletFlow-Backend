using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Api.Filters;
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
    public const string AcceptedMessage =
        "Un e-mail vient d'être envoyé à cette adresse pour finaliser l'inscription.";

    public const string EmailNotSentMessage = "L'e-mail n'a pas pu partir. Réessaie dans quelques minutes.";

    private readonly AccountRegistrationService registrationService;
    private readonly ILogger<AccountRegistrationController> logger;

    // Reçoit le cas d'utilisation de l'inscription et le journal technique.
    public AccountRegistrationController(
        AccountRegistrationService registrationService,
        ILogger<AccountRegistrationController> logger)
    {
        this.registrationService = registrationService;
        this.logger = logger;
    }

    // POST api/account/register : crée un compte et envoie un e-mail de confirmation.
    // Une adresse libre écrit un compte, une adresse confirmée n'écrit rien : la durée minimale cache cette différence.
    [HttpPost("register")]
    [ServiceFilter(typeof(MinimumAnswerDurationFilter))]
    [EnableRateLimiting(RateLimitingConfiguration.SignupPolicy)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
    public async Task<IActionResult> Register(RegisterAccountRequest request)
    {
        var result = await registrationService.RegisterAsync(
            request.Email,
            request.Password);

        if (result is AccountRegistrationResult.RefusedByRules refusal)
        {
            return AnswerRefusedByRules(refusal);
        }

        if (result is AccountRegistrationResult.TooManyRequests)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        if (result is AccountRegistrationResult.EmailCouldNotBeSent emailNotSent)
        {
            return AnswerEmailNotSent(emailNotSent);
        }

        return Ok(new MessageResponse(AcceptedMessage));
    }

    // Note la panne dans le journal, et invite le visiteur à réessayer.
    // Le journal désigne l'utilisateur par son identifiant, jamais par son adresse (SS §7).
    private ObjectResult AnswerEmailNotSent(AccountRegistrationResult.EmailCouldNotBeSent emailNotSent)
    {
        logger.LogError(
            "L'e-mail d'inscription n'a pas pu partir pour l'utilisateur {IdentifiantUtilisateur}.",
            emailNotSent.UserId);

        return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: EmailNotSentMessage);
    }

    // Donne le message de chaque champ refusé.
    private ActionResult AnswerRefusedByRules(AccountRegistrationResult.RefusedByRules refusal)
    {
        if (refusal.IsEmailInvalid)
        {
            ModelState.AddModelError(nameof(RegisterAccountRequest.Email), ErrorMessages.InvalidEmailAddress);
        }

        if (refusal.IsPasswordInvalid)
        {
            ModelState.AddModelError(nameof(RegisterAccountRequest.Password), ErrorMessages.PasswordBreaksRules);
        }

        return ValidationProblem();
    }
}
