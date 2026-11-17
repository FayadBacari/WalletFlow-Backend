using System.ComponentModel.DataAnnotations;

namespace WalletFlow.Api.Contracts;

// Données envoyées pour télécharger ses données : le mot de passe, et le code si la double authentification est activée
// (GR Compte, SS §2). Seuls ces champs sont lus (SS §4).
public class DataExportRequest
{
    [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
    public string Password { get; set; } = string.Empty;

    // Le code de l'application ou un code de secours : seulement si la double authentification est activée.
    public string? Code { get; set; }
}
