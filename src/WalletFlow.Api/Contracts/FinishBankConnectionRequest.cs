namespace WalletFlow.Api.Contracts;

// Données envoyées par la page de retour du site : le code et le state que la banque a mis dans l'adresse (SS §7).
// Seuls ces deux champs sont lus (SS §4).
public class FinishBankConnectionRequest
{
    public string? Code { get; set; }
    public string? State { get; set; }
}
