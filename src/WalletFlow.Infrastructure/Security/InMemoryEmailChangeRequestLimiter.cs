using WalletFlow.Application.Security;

namespace WalletFlow.Infrastructure.Security;

// Compte en mémoire les demandes de changement d'adresse : 3 par heure et par nouvelle adresse (SS §6).
public class InMemoryEmailChangeRequestLimiter : InMemoryEmailAddressRequestLimiter, IEmailChangeRequestLimiter
{
    // Reçoit l'horloge qui mesure la durée de chaque compteur.
    public InMemoryEmailChangeRequestLimiter(TimeProvider clock)
        : base(clock)
    {
    }
}
