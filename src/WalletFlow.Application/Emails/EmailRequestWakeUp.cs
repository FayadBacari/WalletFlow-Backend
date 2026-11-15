namespace WalletFlow.Application.Emails;

// Réveille la tâche de fond des e-mails dès qu'une demande entre dans la file : l'e-mail part sans attendre son passage
// suivant. Un réveil donné pendant que la tâche travaille n'est pas perdu : elle repasse aussitôt après.
public sealed class EmailRequestWakeUp
{
    private readonly object signalLock = new object();
    private readonly SemaphoreSlim signal = new SemaphoreSlim(0, 1);

    // Réveille la tâche. Plusieurs réveils avant qu'elle ne repasse n'en font qu'un.
    public void Signal()
    {
        lock (signalLock)
        {
            if (signal.CurrentCount == 0)
            {
                signal.Release();
            }
        }
    }

    // Attend un réveil, au plus cette durée. Renvoie vrai si la tâche a été réveillée.
    public Task<bool> WaitAsync(TimeSpan maximumWait, CancellationToken cancellationToken)
    {
        return signal.WaitAsync(maximumWait, cancellationToken);
    }
}
