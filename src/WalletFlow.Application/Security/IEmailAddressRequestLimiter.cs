namespace WalletFlow.Application.Security;

// Limite les demandes par adresse e-mail (SS §6), pour qu'on ne puisse pas inonder une boîte mail.
// Implémenté dans la couche Infrastructure.
public interface IEmailAddressRequestLimiter
{
    // Compte une demande de plus pour cette adresse. Renvoie faux si la limite est déjà atteinte.
    bool TryToCountNewRequestForThisEmailAddress(string emailAddressInItsStoredForm);
}
