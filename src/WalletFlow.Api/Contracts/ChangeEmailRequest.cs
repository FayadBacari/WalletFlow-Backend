using System.ComponentModel.DataAnnotations;

namespace WalletFlow.Api.Contracts;

// Données envoyées pour demander une nouvelle adresse e-mail (GR Compte). Seuls ces trois champs sont lus (SS §4).
public class ChangeEmailRequest
{
    [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "La nouvelle adresse e-mail est obligatoire.")]
    public string NewEmail { get; set; } = string.Empty;

    // Le code de l'application ou un code de secours : seulement si la double authentification est activée.
    public string? Code { get; set; }
}
