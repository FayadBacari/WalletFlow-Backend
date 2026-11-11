using WalletFlow.Application.Persistence;

namespace WalletFlow.Tests.Fakes;

// Remplace l'unité de travail dans les tests : les faux dépôts gardent ensemble tout ce qui a été préparé, ou rien,
// comme une vraie transaction. Une panne ou un conflit simulé remet tout comme avant l'enregistrement.
public class FakeDatabaseUnitOfWork : IDatabaseUnitOfWork
{
    private readonly List<ITransactionalFakeRepository> repositories;

    public int CommittedTransactionCount { get; private set; } = 0;

    // Reçoit les faux dépôts dont les changements partent ensemble.
    public FakeDatabaseUnitOfWork(params ITransactionalFakeRepository[] repositories)
    {
        this.repositories = new List<ITransactionalFakeRepository>(repositories);
    }

    // Garde tout, sauf panne ou conflit simulé. Une panne est remontée, comme celle de la vraie base, après avoir tout annulé.
    public Task<bool> TrySaveChangesAsync()
    {
        try
        {
            foreach (var repository in repositories)
            {
                repository.ThrowIfTableIsDown();
            }
        }
        catch (InvalidOperationException)
        {
            RollbackAll();
            throw;
        }

        foreach (var repository in repositories)
        {
            if (repository.HasConcurrentChange())
            {
                RollbackAll();
                repository.ApplyConcurrentChanges();
                return Task.FromResult(false);
            }
        }

        foreach (var repository in repositories)
        {
            repository.Commit();
        }

        CommittedTransactionCount = CommittedTransactionCount + 1;
        return Task.FromResult(true);
    }

    // Annule la transaction simulée dans tous les faux dépôts.
    private void RollbackAll()
    {
        foreach (var repository in repositories)
        {
            repository.Rollback();
        }
    }
}
