using System.ComponentModel.DataAnnotations;

namespace WalletFlow.Api.Contracts;

// Données envoyées par la page de déblocage : le jeton lu dans le lien. Seul ce champ est lu (SS §4).
public class UnlockAccountRequest
{
    [Required(ErrorMessage = "Le lien de déblocage est obligatoire.")]
    public string Token { get; set; } = string.Empty;
}
