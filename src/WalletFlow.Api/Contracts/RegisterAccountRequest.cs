using System.ComponentModel.DataAnnotations;

namespace WalletFlow.Api.Contracts;

// Données envoyées par un visiteur pour s'inscrire. Seuls ces deux champs sont lus :
// personne ne peut choisir son rôle en ajoutant un champ à sa requête (SS §4).
public class RegisterAccountRequest
{
    [Required(ErrorMessage = "L'adresse e-mail est obligatoire.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
    public string Password { get; set; } = string.Empty;
}
