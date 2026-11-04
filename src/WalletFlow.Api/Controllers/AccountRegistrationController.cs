using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.UserAccounts;

namespace WalletFlow.Api.Controllers;

// Inscription d'un visiteur (GR Auth).
[ApiController]
[Route("api/account")]
public class AccountRegistrationController : ControllerBase
{
    // Le même message pour toute inscription acceptée, que l'adresse soit libre ou déjà utilisée (SS §2).
    public const string MessageSentForEveryAcceptedRegistration =
        "Un e-mail vient d'être envoyé à cette adresse pour finaliser l'inscription.";

    // La plus longue adresse et le plus long mot de passe permis tiennent dans cette taille,
    // même si chaque caractère est écrit en \uXXXX dans le JSON.
    private const long MaximumNumberOfBytesInRegistrationRequest = 4096;

    private readonly AccountRegistrationService serviceThatRegistersTheNewUserAccounts;

    // Reçoit le cas d'utilisation de l'inscription.
    public AccountRegistrationController(AccountRegistrationService serviceThatRegistersTheNewUserAccounts)
    {
        this.serviceThatRegistersTheNewUserAccounts = serviceThatRegistersTheNewUserAccounts;
    }

    // POST api/account/register : crée un compte et envoie un e-mail de confirmation.
    // Le paramètre est déclaré nullable pour qu'ASP.NET Core ne cite pas son nom dans l'erreur d'une requête
    // illisible (SS §10). Il n'est jamais null ici : Disallow refuse une requête vide ou « null » avant cette fonction.
    [HttpPost("register")]
    [EnableRateLimiting(RateLimitingConfiguration.PolicyNameForSignupRequestsPerIpAddress)]
    [RequestSizeLimit(MaximumNumberOfBytesInRegistrationRequest)]
    public async Task<IActionResult> RegisterNewAccountFromVisitorRequest(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] RegisterAccountRequest? requestSentByTheVisitorToRegister)
    {
        var resultOfTheVisitorAccountRegistration = await serviceThatRegistersTheNewUserAccounts.RegisterNewAccountForVisitorAsync(
            requestSentByTheVisitorToRegister!.Email,
            requestSentByTheVisitorToRegister.Password);

        if (resultOfTheVisitorAccountRegistration.IsAccepted)
        {
            return Ok(new MessageResponse(MessageSentForEveryAcceptedRegistration));
        }

        if (resultOfTheVisitorAccountRegistration.EmailAddressErrorMessage != null)
        {
            ModelState.AddModelError(nameof(RegisterAccountRequest.Email), resultOfTheVisitorAccountRegistration.EmailAddressErrorMessage);
        }

        if (resultOfTheVisitorAccountRegistration.PasswordErrorMessage != null)
        {
            ModelState.AddModelError(nameof(RegisterAccountRequest.Password), resultOfTheVisitorAccountRegistration.PasswordErrorMessage);
        }

        return ValidationProblem(ModelState);
    }
}
