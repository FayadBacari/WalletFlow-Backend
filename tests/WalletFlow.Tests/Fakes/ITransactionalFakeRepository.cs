namespace WalletFlow.Tests.Fakes;

// Un faux dépôt dont les changements partent ensemble, comme dans une transaction : voir FakeDatabaseUnitOfWork.
public interface ITransactionalFakeRepository
{
    // Lance la panne simulée si les changements préparés touchent une table en panne.
    void ThrowIfTableIsDown();

    // Renvoie vrai si une autre requête simulée change les mêmes lignes juste avant cet enregistrement.
    bool HasConcurrentChange();

    // Remet tout comme avant, comme une transaction annulée : les objets lus reprennent leurs valeurs, et rien de préparé n'est gardé.
    void Rollback();

    // Écrit ce que l'autre requête simulée a enregistré, une fois la transaction annulée.
    void ApplyConcurrentChanges();

    // Garde tout, comme une transaction validée.
    void Commit();
}
