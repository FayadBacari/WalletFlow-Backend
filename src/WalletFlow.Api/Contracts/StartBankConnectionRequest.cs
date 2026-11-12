namespace WalletFlow.Api.Contracts;

// Données envoyées pour connecter une banque : son nom, tel que la liste des banques le donne (GR Comptes).
// Seul ce champ est lu (SS §4).
public class StartBankConnectionRequest
{
    public string? BankName { get; set; }
}
