using System.ComponentModel.DataAnnotations;

namespace WalletFlow.Api.Contracts;

// Données envoyées pour désactiver la double authentification (GR Auth). Seuls ces deux champs sont lus (SS §4).
public class DeactivateTwoFactorRequest
{
    [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Le code est obligatoire.")]
    public string Code { get; set; } = string.Empty;
}
