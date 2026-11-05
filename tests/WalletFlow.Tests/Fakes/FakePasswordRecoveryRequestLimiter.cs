using WalletFlow.Application.Security;

namespace WalletFlow.Tests.Fakes;

// Remplace la limite de la récupération dans les tests : garde les adresses comptées en mémoire.
public class FakePasswordRecoveryRequestLimiter : FakeEmailAddressRequestLimiter, IPasswordRecoveryRequestLimiter
{
}
