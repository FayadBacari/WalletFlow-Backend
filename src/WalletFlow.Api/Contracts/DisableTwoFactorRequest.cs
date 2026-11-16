using System.ComponentModel.DataAnnotations;

namespace WalletFlow.Api.Contracts;

// Données envoyées par la page du lien qui désactive la double authentification : le jeton lu dans le lien et le mot de passe
// du compte. Seuls ces deux champs sont lus (SS §4).
public class DisableTwoFactorRequest
{
    [Required(ErrorMessage = "Le lien est obligatoire.")]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
    public string Password { get; set; } = string.Empty;
}
