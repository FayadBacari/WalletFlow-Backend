using WalletFlow.Application.Security;

namespace WalletFlow.Infrastructure.Security;

// Compte en mémoire les demandes de récupération : 3 par heure et par adresse e-mail (SS §6).
public class InMemoryPasswordRecoveryRequestLimiter : InMemoryEmailAddressRequestLimiter, IPasswordRecoveryRequestLimiter
{
    // Reçoit l'horloge qui mesure la durée de chaque compteur.
    public InMemoryPasswordRecoveryRequestLimiter(TimeProvider clockThatGivesTheCurrentDateAndTime)
        : base(clockThatGivesTheCurrentDateAndTime)
    {
    }
}
