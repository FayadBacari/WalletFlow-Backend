using WalletFlow.Application.Security;

namespace WalletFlow.Tests.Fakes;

// Remplace la limite par adresse e-mail dans les tests : garde les adresses comptées en mémoire.
public class FakeEmailAddressRequestLimiter : IEmailAddressRequestLimiter
{
    public List<string> EmailAddressesCounted { get; } = new List<string>();

    // Simule une limite déjà atteinte.
    public bool EveryRequestIsRefusedDuringThisTest { get; set; } = false;

    // Garde l'adresse en mémoire, puis accepte ou refuse selon le test.
    public bool TryToCountNewRequestForThisEmailAddress(string emailAddressInItsStoredForm)
    {
        EmailAddressesCounted.Add(emailAddressInItsStoredForm);

        return !EveryRequestIsRefusedDuringThisTest;
    }
}
