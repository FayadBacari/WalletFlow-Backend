using System.ComponentModel.DataAnnotations;

namespace WalletFlow.Api.Contracts;

// Données envoyées pour demander la suppression du compte : le mot de passe, et le code si la double authentification est
// activée (GR Suppression, SS §2). Le site demande de confirmer avant de les envoyer. Seuls ces champs sont lus (SS §4).
public class AccountDeletionRequest
{
    [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
    public string Password { get; set; } = string.Empty;

    // Le code de l'application ou un code de secours : seulement si la double authentification est activée.
    public string? Code { get; set; }
}

// La réponse d'une demande de suppression : le message, et le jour de Paris où le compte sera effacé (GR Suppression).
public record AccountDeletionResponse(string Message, DateOnly ErasureDate);
