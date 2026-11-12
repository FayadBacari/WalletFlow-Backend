namespace WalletFlow.Application.BankConnections;

// Le prestataire bancaire a refusé, ou n'a pas répondu. Failure dit ce que l'application doit en faire.
public class BankProviderException : Exception
{
    public BankProviderFailure Failure { get; }

    // Reçoit la raison, un message technique sans aucun secret, et l'erreur d'origine s'il y en a une.
    public BankProviderException(BankProviderFailure failure, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }
}
