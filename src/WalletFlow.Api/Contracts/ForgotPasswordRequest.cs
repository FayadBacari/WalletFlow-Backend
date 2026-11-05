using System.ComponentModel.DataAnnotations;

namespace WalletFlow.Api.Contracts;

// Données envoyées par un visiteur qui a oublié son mot de passe. Seul ce champ est lu (SS §4).
public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "L'adresse e-mail est obligatoire.")]
    public string Email { get; set; } = string.Empty;
}
