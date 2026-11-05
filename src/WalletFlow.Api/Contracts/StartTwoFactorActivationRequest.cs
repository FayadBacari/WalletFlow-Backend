using System.ComponentModel.DataAnnotations;

namespace WalletFlow.Api.Contracts;

// Données envoyées pour commencer l'activation : le mot de passe redemandé (SS §2). Seul ce champ est lu (SS §4).
public class StartTwoFactorActivationRequest
{
    [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
    public string Password { get; set; } = string.Empty;
}
