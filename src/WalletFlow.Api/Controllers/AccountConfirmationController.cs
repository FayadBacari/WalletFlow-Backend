using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.UserAccounts;

namespace WalletFlow.Api.Controllers;

// Confirmation de l'adresse e-mail d'un nouveau compte (GR Auth).
[ApiController]
[Route("api/account")]
public class AccountConfirmationController : ControllerBase
{
    public const string MessageSentWhenTheAccountIsConfirmed = "Ton adresse e-mail est confirmée. Tu peux maintenant te connecter.";

    private readonly AccountConfirmationService serviceThatConfirmsTheNewUserAccounts;

    // Reçoit le cas d'utilisation de la confirmation.
    public AccountConfirmationController(AccountConfirmationService serviceThatConfirmsTheNewUserAccounts)
    {
        this.serviceThatConfirmsTheNewUserAccounts = serviceThatConfirmsTheNewUserAccounts;
    }

    // POST api/account/confirm-email : active le compte, avec le jeton du lien et le mot de passe.
    [HttpPost("confirm-email")]
    [EnableRateLimiting(RateLimitingConfiguration.PolicyNameForConfirmationAttemptsPerIpAddress)]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumNumberOfBytesInOneAccountRequest)]
    public async Task<IActionResult> ConfirmNewAccountFromTheVisitorRequest(ConfirmAccountRequest requestSentByTheVisitorToConfirm)
    {
        var resultOfTheConfirmationOfTheAccount = await serviceThatConfirmsTheNewUserAccounts.ConfirmAccountWithLinkTokenAndPasswordAsync(
            requestSentByTheVisitorToConfirm.Token,
            requestSentByTheVisitorToConfirm.Password);

        if (resultOfTheConfirmationOfTheAccount.IsConfirmed)
        {
            return Ok(new MessageResponse(MessageSentWhenTheAccountIsConfirmed));
        }

        if (resultOfTheConfirmationOfTheAccount.LinkErrorMessage != null)
        {
            ModelState.AddModelError(nameof(ConfirmAccountRequest.Token), resultOfTheConfirmationOfTheAccount.LinkErrorMessage);
        }

        if (resultOfTheConfirmationOfTheAccount.PasswordErrorMessage != null)
        {
            ModelState.AddModelError(nameof(ConfirmAccountRequest.Password), resultOfTheConfirmationOfTheAccount.PasswordErrorMessage);
        }

        return ValidationProblem();
    }
}
