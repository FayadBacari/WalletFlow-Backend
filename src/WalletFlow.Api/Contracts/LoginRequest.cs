using System.ComponentModel.DataAnnotations;

namespace WalletFlow.Api.Contracts;

// Données envoyées par un visiteur pour se connecter. Seuls ces deux champs sont lus (SS §4).
public class LoginRequest
{
    [Required(ErrorMessage = "L'adresse e-mail est obligatoire.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
    public string Password { get; set; } = string.Empty;
}
