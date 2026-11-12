namespace WalletFlow.Api.Contracts;

// Données envoyées pour changer le nom et le prénom (GR Compte). Un champ absent ou vide efface ce qu'il y avait.
// Seuls ces deux champs sont lus (SS §4).
public class ChangePersonalNameRequest
{
    public string? LastName { get; set; }

    public string? FirstName { get; set; }
}
