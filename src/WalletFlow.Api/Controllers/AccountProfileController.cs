using Microsoft.AspNetCore.Mvc;
using WalletFlow.Api.Authentication;
using WalletFlow.Api.Configuration;
using WalletFlow.Api.Contracts;
using WalletFlow.Application.AccountSettings;

namespace WalletFlow.Api.Controllers;

// Profil de l'utilisateur connecté : sa lecture, et le changement du nom et du prénom (GR Compte).
// Ces routes n'ont aucun attribut de sécurité : toute route demande par défaut une session et le jeton anti-CSRF (voir Program.cs).
[ApiController]
[Route("api/account")]
public class AccountProfileController : ControllerBase
{
    public const string NameSavedMessage = "Ton nom et ton prénom sont enregistrés.";

    private readonly ProfileService profileService;
    private readonly PersonalNameChangeService nameChangeService;

    // Reçoit les cas d'utilisation du profil.
    public AccountProfileController(ProfileService profileService, PersonalNameChangeService nameChangeService)
    {
        this.profileService = profileService;
        this.nameChangeService = nameChangeService;
    }

    // GET api/account/profile : l'adresse, le nom, le prénom et l'état de la double authentification.
    // Ce sont des données personnelles : aucun navigateur ni intermédiaire ne doit les garder.
    [HttpGet("profile")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetProfile()
    {
        var profile = await profileService.FindAsync(SessionClaims.ReadUserId(User));

        // Les sessions d'un compte disparaissent avec lui : ce cas ne vient que d'un effacement pendant la requête.
        if (profile == null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: UserSessionAuthenticationHandler.LoginRequiredMessage);
        }

        return Ok(new AccountProfileResponse(profile.Email, profile.LastName, profile.FirstName, profile.IsTwoFactorEnabled));
    }

    // POST api/account/name : enregistre le nom et le prénom. Un champ vide efface ce qu'il y avait.
    [HttpPost("name")]
    [RequestSizeLimit(RequestSizeConfiguration.MaximumAccountRequestBytes)]
    public async Task<IActionResult> ChangeName(ChangePersonalNameRequest request)
    {
        var result = await nameChangeService.ChangeAsync(
            SessionClaims.ReadUserId(User),
            request.LastName,
            request.FirstName);

        if (result is PersonalNameChangeResult.Changed)
        {
            return Ok(new MessageResponse(NameSavedMessage));
        }

        if (result is PersonalNameChangeResult.RefusedByRules refusal)
        {
            return AnswerRefusedByRules(refusal);
        }

        return Problem(statusCode: StatusCodes.Status409Conflict, title: ErrorMessages.AccountChangedMeanwhile);
    }

    // Donne le message de chaque champ refusé.
    private ActionResult AnswerRefusedByRules(PersonalNameChangeResult.RefusedByRules refusal)
    {
        if (refusal.IsLastNameInvalid)
        {
            ModelState.AddModelError(nameof(ChangePersonalNameRequest.LastName), ErrorMessages.PersonalNameBreaksRules);
        }

        if (refusal.IsFirstNameInvalid)
        {
            ModelState.AddModelError(nameof(ChangePersonalNameRequest.FirstName), ErrorMessages.PersonalNameBreaksRules);
        }

        return ValidationProblem();
    }
}
