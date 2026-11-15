using System.Text.Json.Serialization;

namespace WalletFlow.Api.Contracts;

// Données envoyées pour choisir le canal des alertes (GR Notif) : « email », « phone » ou « emailAndPhone ».
// Seul ce champ est lu (SS §4).
public class AlertChannelRequest
{
    public string? Channel { get; set; }
}

// Données envoyées pour activer ou désactiver un type d'alerte (GR Notif). Seul ce champ est lu (SS §4).
// Il doit être écrit : une requête vide ne coupe jamais une alerte par erreur.
public class AlertSwitchRequest
{
    [JsonRequired]
    public bool IsEnabled { get; set; }
}
