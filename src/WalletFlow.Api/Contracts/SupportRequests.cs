namespace WalletFlow.Api.Contracts;

// Données envoyées par l'utilisateur connecté qui écrit au support (GR Admin). Seul ce champ est lu (SS §4).
public class SupportMessageRequest
{
    public string? Message { get; set; }
}

// Données envoyées par un visiteur qui écrit au support : l'adresse où lui répondre, et son message (GR Admin).
// Seuls ces champs sont lus (SS §4).
public class VisitorSupportMessageRequest
{
    public string? ContactEmail { get; set; }
    public string? Message { get; set; }
}
