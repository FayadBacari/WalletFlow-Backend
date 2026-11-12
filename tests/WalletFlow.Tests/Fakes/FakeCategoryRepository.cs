using WalletFlow.Application.Categories;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde les catégories, les transactions et les règles de classement en mémoire.
// Une catégorie préparée n'est gardée qu'avec les autres changements (voir FakeDatabaseUnitOfWork).
// La comparaison des noms, faite par lower() dans la vraie base, est testée sur la vraie base (CategoryRepositoryTests).
public class FakeCategoryRepository : ICategoryRepository, ITransactionalFakeRepository
{
    private readonly object pendingChangesLock = new object();
    private readonly List<Category> preparedCategories = new List<Category>();
    private readonly ReadCopies<Category> readCategories = new ReadCopies<Category>();
    private Action? concurrentChanges = null;

    // Ce que la fausse base contient. La liste des catégories peut être celle du faux dépôt des comptes, où l'inscription écrit.
    public List<Category> SavedCategories { get; }
    public List<BankTransaction> SavedTransactions { get; } = new List<BankTransaction>();
    public List<CategorizationRule> SavedRules { get; } = new List<CategorizationRule>();

    // Ce que les tests vérifient : les catégories réécrites pour contrôler leur version, et les recherches faites.
    public List<Category> CheckedCategories { get; } = new List<Category>();
    public int SearchCount { get; private set; } = 0;

    // Simule une autre requête qui crée cette catégorie juste avant l'enregistrement d'une création :
    // sa création a réécrit « Autres », dont la version a changé.
    public Category? CategoryCreatedBeforeSave { get; set; } = null;

    // Simule une autre requête qui supprime la catégorie lue, juste avant l'enregistrement de son changement.
    public bool DeleteReadCategoryBeforeSave { get; set; } = false;

    // Simule une autre requête qui supprime la catégorie entre sa lecture et sa suppression.
    public bool CategoryDisappearsBeforeDeletion { get; set; } = false;

    // Simule d'autres requêtes qui écrivent les mêmes catégories juste avant chaque enregistrement : un conflit chacune.
    public int Conflicts { get; set; } = 0;

    // Crée une fausse base vide.
    public FakeCategoryRepository()
    {
        SavedCategories = new List<Category>();
    }

    // Crée une fausse base qui partage cette liste de catégories, par exemple avec le faux dépôt des comptes.
    public FakeCategoryRepository(List<Category> savedCategories)
    {
        SavedCategories = savedCategories;
    }

    // Donne les catégories gardées pour cet utilisateur.
    public Task<List<Category>> FindAllAsync(Guid userId)
    {
        lock (pendingChangesLock)
        {
            SearchCount = SearchCount + 1;
            var categories = SavedCategories.Where(category => category.UserId == userId).ToList();

            foreach (var category in categories)
            {
                readCategories.Keep(category);
            }

            return Task.FromResult(categories);
        }
    }

    // Cherche la catégorie parmi celles de l'utilisateur.
    public Task<Category?> FindAsync(Guid userId, Guid categoryId)
    {
        lock (pendingChangesLock)
        {
            SearchCount = SearchCount + 1;
            var category = SavedCategories.SingleOrDefault(saved => saved.Id == categoryId && saved.UserId == userId);

            if (category != null)
            {
                readCategories.Keep(category);
            }

            return Task.FromResult(category);
        }
    }

    // Cherche la catégorie « Autres » de l'utilisateur.
    public Task<Category?> FindOtherCategoryAsync(Guid userId)
    {
        lock (pendingChangesLock)
        {
            SearchCount = SearchCount + 1;
            var category = SavedCategories.SingleOrDefault(saved => saved.UserId == userId && saved.IsOtherCategory);

            if (category != null)
            {
                readCategories.Keep(category);
            }

            return Task.FromResult(category);
        }
    }

    // Compte les catégories gardées pour cet utilisateur.
    public Task<int> CountAsync(Guid userId)
    {
        lock (pendingChangesLock)
        {
            return Task.FromResult(SavedCategories.Count(category => category.UserId == userId));
        }
    }

    // Compare les noms sans les majuscules, comme la vraie base, sans compter la catégorie renommée.
    public Task<bool> IsNameUsedAsync(Guid userId, string name, Guid? renamedCategoryId)
    {
        lock (pendingChangesLock)
        {
            var isUsed = SavedCategories.Any(category =>
                category.UserId == userId
                && category.Id != renamedCategoryId
                && category.Name.ToLowerInvariant() == name.ToLowerInvariant());

            return Task.FromResult(isUsed);
        }
    }

    // Garde la catégorie de côté : elle ne sera gardée qu'avec les autres changements.
    public void PrepareCategory(Category category)
    {
        lock (pendingChangesLock)
        {
            preparedCategories.Add(category);
        }
    }

    // Note la catégorie réécrite : les conflits de ce faux dépôt sont simulés par ses réglages.
    public void PrepareUnchangedCheck(Category category)
    {
        lock (pendingChangesLock)
        {
            CheckedCategories.Add(category);
        }
    }

    // Supprime la catégorie, range ses transactions dans « Autres » et efface ses règles, comme la vraie base.
    public Task<bool> DeleteAsync(Guid userId, Guid categoryId)
    {
        lock (pendingChangesLock)
        {
            if (CategoryDisappearsBeforeDeletion)
            {
                SavedCategories.RemoveAll(category => category.Id == categoryId);
            }

            var deletedCategory = SavedCategories.SingleOrDefault(category =>
                category.Id == categoryId
                && category.UserId == userId
                && !category.IsOtherCategory);

            if (deletedCategory == null)
            {
                return Task.FromResult(false);
            }

            var otherCategory = SavedCategories.Single(category => category.UserId == userId && category.IsOtherCategory);
            SavedCategories.Remove(deletedCategory);
            SavedRules.RemoveAll(rule => rule.CategoryId == categoryId);

            foreach (var transaction in SavedTransactions.Where(transaction => transaction.CategoryId == categoryId))
            {
                EntityValues.WriteProperty(transaction, nameof(BankTransaction.CategoryId), otherCategory.Id);
            }

            return Task.FromResult(true);
        }
    }

    // Aucune table de ce faux dépôt ne tombe en panne pendant un enregistrement.
    public void ThrowIfTableIsDown()
    {
    }

    // Décide si une autre requête écrit les mêmes catégories juste avant l'enregistrement, selon les réglages du test,
    // et prépare ce que cette autre requête aura écrit.
    public bool HasConcurrentChange()
    {
        lock (pendingChangesLock)
        {
            var changedCategory = readCategories.FindChangedSinceRead((copy, current) =>
                copy.Name != current.Name || copy.MonthlyBudgetInCents != current.MonthlyBudgetInCents);
            var isWriting = preparedCategories.Count > 0 || changedCategory != null;

            var createdCategory = CategoryCreatedBeforeSave;
            if (createdCategory != null && preparedCategories.Count > 0)
            {
                CategoryCreatedBeforeSave = null;
                concurrentChanges = () => SavedCategories.Add(createdCategory);
                return true;
            }

            if (DeleteReadCategoryBeforeSave && changedCategory != null)
            {
                DeleteReadCategoryBeforeSave = false;
                var deletedId = changedCategory.Id;
                concurrentChanges = () => SavedCategories.RemoveAll(category => category.Id == deletedId);
                return true;
            }

            if (Conflicts > 0 && isWriting)
            {
                Conflicts = Conflicts - 1;
                return true;
            }

            return false;
        }
    }

    // Remet les catégories lues comme elles étaient, et oublie les catégories préparées.
    public void Rollback()
    {
        lock (pendingChangesLock)
        {
            readCategories.RestoreAll();
            ClearTracking();
        }
    }

    // Écrit ce que l'autre requête simulée a enregistré.
    public void ApplyConcurrentChanges()
    {
        Action? changes;

        lock (pendingChangesLock)
        {
            changes = concurrentChanges;
            concurrentChanges = null;
        }

        if (changes != null)
        {
            lock (pendingChangesLock)
            {
                changes();
            }
        }
    }

    // Garde les catégories préparées.
    public void Commit()
    {
        lock (pendingChangesLock)
        {
            SavedCategories.AddRange(preparedCategories);
            ClearTracking();
        }
    }

    // Oublie ce qui a été lu et préparé depuis le dernier enregistrement.
    private void ClearTracking()
    {
        preparedCategories.Clear();
        readCategories.Clear();
    }
}
