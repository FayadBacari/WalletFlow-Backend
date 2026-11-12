using System.Text.Json.Serialization;

namespace WalletFlow.Api.Contracts;

// Données envoyées pour marquer une opération comme transfert entre deux comptes de l'utilisateur, ou retirer cette marque
// (GR Comptes). Le champ doit être écrit : une requête vide ne retire jamais une marque par erreur. Seul ce champ est lu (SS §4).
public class TransactionTransferRequest
{
    [JsonRequired]
    public bool IsInternalTransfer { get; set; }
}
