using System.ComponentModel.DataAnnotations;

namespace WalletFlow.Api.Contracts;

// Données envoyées par la page du nouveau mot de passe : le jeton lu dans le lien et le mot de passe choisi.
// Seuls ces deux champs sont lus (SS §4).
public class ResetPasswordRequest
{
    [Required(ErrorMessage = "Le lien de récupération est obligatoire.")]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Le nouveau mot de passe est obligatoire.")]
    public string NewPassword { get; set; } = string.Empty;
}
