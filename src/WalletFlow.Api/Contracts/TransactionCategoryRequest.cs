using System.Text.Json.Serialization;

namespace WalletFlow.Api.Contracts;

// Données envoyées pour ranger une opération dans une autre catégorie (GR Dépenses). ApplyToFutureTransactions dit
// si le même choix vaut aussi pour les prochaines opérations qui ont le même libellé. Les deux champs doivent être écrits :
// une requête incomplète ne crée jamais une règle, ni n'en oublie une, par erreur. Seuls ces champs sont lus (SS §4).
public class TransactionCategoryRequest
{
    [JsonRequired]
    public Guid CategoryId { get; set; }

    [JsonRequired]
    public bool ApplyToFutureTransactions { get; set; }
}
