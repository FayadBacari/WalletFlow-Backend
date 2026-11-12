namespace WalletFlow.Application.Security;

// Limite les demandes de changement d'adresse par nouvelle adresse (SS §6). Son compteur est séparé des autres :
// sinon, des inscriptions avec l'adresse d'une victime l'empêcheraient de la choisir comme nouvelle adresse.
public interface IEmailChangeRequestLimiter : IEmailAddressRequestLimiter
{
}
