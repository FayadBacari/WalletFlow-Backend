using System.ComponentModel.DataAnnotations;

namespace WalletFlow.Api.Contracts;

// Données envoyées pour changer de mot de passe (GR Compte). Seuls ces trois champs sont lus (SS §4).
public class ChangePasswordRequest
{
    [Required(ErrorMessage = "Le mot de passe actuel est obligatoire.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Le nouveau mot de passe est obligatoire.")]
    public string NewPassword { get; set; } = string.Empty;

    // Le code de l'application ou un code de secours : seulement si la double authentification est activée.
    public string? Code { get; set; }
}
