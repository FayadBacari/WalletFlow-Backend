namespace WalletFlow.Domain.NotificationsAndSupport;

// Les essais d'envoi d'un e-mail de la file (SS §9, DD §12) : 5 au plus. Après un échec, l'essai suivant attend 1 min,
// 5 min, 30 min, puis 2 h : une courte panne du service d'e-mails ne perd rien, et une longue panne n'insiste pas sans fin.
public static class EmailRequestRules
{
    public const int MaximumTries = 5;

    // L'attente après le 1er, le 2e, le 3e, puis le 4e échec. Le 5e échec abandonne la demande.
    private static readonly TimeSpan[] WaitsAfterFailure =
    {
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2)
    };

    // Donne l'attente avant l'essai suivant, après ce nombre d'échecs, de 1 à 4 : après le 5e, il n'y a plus d'essai.
    public static TimeSpan FindWaitAfterFailures(int failedTryCount)
    {
        if (failedTryCount < 1 || failedTryCount >= MaximumTries)
        {
            throw new ArgumentOutOfRangeException(nameof(failedTryCount));
        }

        return WaitsAfterFailure[failedTryCount - 1];
    }
}
