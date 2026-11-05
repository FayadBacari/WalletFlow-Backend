using System.ComponentModel.DataAnnotations;

namespace WalletFlow.Api.Contracts;

// Données envoyées avec un code de double authentification. Seul ce champ est lu (SS §4).
public class TwoFactorCodeRequest
{
    [Required(ErrorMessage = "Le code est obligatoire.")]
    public string Code { get; set; } = string.Empty;
}
