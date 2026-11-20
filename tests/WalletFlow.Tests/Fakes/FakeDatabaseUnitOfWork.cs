using WalletFlow.Application.Persistence;

namespace WalletFlow.Tests.Fakes;

// Remplace l'unité de travail dans les tests : les faux dépôts gardent ensemble tout ce qui a été préparé, ou rien,
// comme une vraie transaction. Une panne ou un conflit simulé remet tout comme avant l'enregistrement. Comme dans la vraie
// base, une transaction est enregistrée en entier avant la suivante, et ce qui la suit, comme le réveil de la file d'e-mails,
// ne part qu'une fois tout enregistré. Dans une API de test, chaque portée de services a sa propre unité de travail, sur ses
// propres vues des faux dépôts, comme un DbContext par requête : toutes partagent le verrou des enregistrements et leur compte.
public class FakeDatabaseUnitOfWork : IDatabaseUnitOfWork
{
    private readonly List<ITransactionalFakeRepository> repositories;
    private readonly Commits commits;

    // Ce que partagent toutes les unités de travail d'une même fausse base.
    private sealed class Commits
    {
        public object Gate { get; } = new object();

        public int Count { get; set; } = 0;
    }

    // Reçoit les faux dépôts dont les changements partent ensemble.
    public FakeDatabaseUnitOfWork(params ITransactionalFakeRepository[] repositories)
        : this(new Commits(), repositories)
    {
    }

    // Reçoit ce que partagent les unités de travail de la même fausse base, et les vues des faux dépôts de cette transaction.
    private FakeDatabaseUnitOfWork(Commits commits, ITransactionalFakeRepository[] repositories)
    {
        this.commits = commits;
        this.repositories = new List<ITransactionalFakeRepository>(repositories);
    }

    // Le nombre de transactions enregistrées, par toutes les unités de travail de la même fausse base.
    public int CommittedTransactionCount => commits.Count;

    // Crée l'unité de travail d'une autre portée, sur ces vues des faux dépôts : elle partage le verrou et le compte.
    public FakeDatabaseUnitOfWork CreateView(params ITransactionalFakeRepository[] repositoryViews)
    {
        return new FakeDatabaseUnitOfWork(commits, repositoryViews);
    }

    // Garde tout, sauf panne ou conflit simulé. Une panne est remontée, comme celle de la vraie base, après avoir tout annulé.
    public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default)
    {
        lock (commits.Gate)
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

            foreach (var repository in repositories)
            {
                repository.AfterCommit();
            }

            commits.Count = commits.Count + 1;
            return Task.FromResult(true);
        }
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
