using System.Text.Json.Serialization;

namespace WalletFlow.Api.Contracts;

// Données envoyées pour accepter une suggestion (GR Sacs) : le sac qui reçoit l'argent, et le montant en centimes, de 1 centime
// au montant de la suggestion. Les deux champs doivent être écrits : une requête incomplète ne place jamais rien par erreur.
// Seuls ces champs sont lus (SS §4).
public class SuggestionAcceptanceRequest
{
    [JsonRequired]
    public Guid BagId { get; set; }

    [JsonRequired]
    public int AmountInCents { get; set; }
}
