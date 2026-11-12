using WalletFlow.Application.Security;

namespace WalletFlow.Tests.Fakes;

// Remplace la limite des changements d'adresse dans les tests : garde les nouvelles adresses comptées en mémoire.
public class FakeEmailChangeRequestLimiter : FakeEmailAddressRequestLimiter, IEmailChangeRequestLimiter
{
}
