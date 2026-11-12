namespace WalletFlow.Api.Contracts;

// Données envoyées pour créer ou renommer une catégorie (GR Dépenses). Seul ce champ est lu (SS §4) :
// un champ « Autres » ou un autre utilisateur ajouté à la requête est ignoré.
public class CategoryNameRequest
{
    public string? Name { get; set; }
}
