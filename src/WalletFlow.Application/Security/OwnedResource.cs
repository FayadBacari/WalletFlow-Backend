namespace WalletFlow.Application.Security;

// Les sortes de ressources qu'un utilisateur possède, et qu'une requête peut viser par leur identifiant (SS §4).
public enum OwnedResource
{
    Category,
    Transaction,
    BankConnection,
    BankAccount,
    Bag
}
