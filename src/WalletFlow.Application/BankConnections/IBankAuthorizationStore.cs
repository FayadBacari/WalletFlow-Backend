namespace WalletFlow.Application.BankConnections;

// Les autorisations commencées qui attendent le retour de la banque, 15 minutes au plus (SS §7). Implémenté dans la couche Infrastructure.
public interface IBankAuthorizationStore
{
    // Garde une autorisation commencée. Seule l'empreinte de son paramètre state sert à la retrouver.
    void Save(string stateHash, PendingBankAuthorization authorization);

    // Reprend l'autorisation de ce state, une seule fois, si elle n'a pas expiré et si elle appartient à cet utilisateur. Sinon null.
    // Un state présenté par un autre utilisateur est oublié quand même : un lien piégé ne sert jamais deux fois.
    PendingBankAuthorization? TryTake(string stateHash, Guid userId);
}
