namespace WalletFlow.Application.Security;

// Limite les demandes de récupération par adresse e-mail (SS §6). Son compteur est séparé de celui de l'inscription :
// sinon, envoyer des inscriptions avec l'adresse d'une victime l'empêcherait de retrouver son mot de passe.
public interface IPasswordRecoveryRequestLimiter : IEmailAddressRequestLimiter
{
}
