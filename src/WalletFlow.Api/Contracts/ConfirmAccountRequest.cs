using System.ComponentModel.DataAnnotations;

namespace WalletFlow.Api.Contracts;

// Données envoyées par la page de confirmation : le jeton lu dans le lien et le mot de passe saisi.
// Seuls ces deux champs sont lus (SS §4).
public class ConfirmAccountRequest
{
    [Required(ErrorMessage = "Le lien de confirmation est obligatoire.")]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
    public string Password { get; set; } = string.Empty;
}
