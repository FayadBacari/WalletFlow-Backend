using WalletFlow.Application.Categories;
using WalletFlow.Application.Persistence;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Crée les cas d'utilisation des catégories avec une fausse base, pour un utilisateur qui a ses 8 catégories par défaut.
public class CategoryServices
{
    public Guid UserId { get; } = Guid.NewGuid();
    public FakeCategoryRepository Repository { get; } = new FakeCategoryRepository();
    public FakeDatabaseUnitOfWork UnitOfWork { get; }

    // Relie la fausse base à la fausse unité de travail, et donne à l'utilisateur ses catégories par défaut.
    public CategoryServices()
    {
        UnitOfWork = new FakeDatabaseUnitOfWork(Repository);
        Repository.SavedCategories.AddRange(DefaultCategories.CreateForUser(UserId));
    }

    // Crée le service qui liste les catégories.
    public CategoryListService CreateList()
    {
        return new CategoryListService(Repository);
    }

    // Crée le service qui crée une catégorie.
    public CategoryCreationService CreateCreation()
    {
        return new CategoryCreationService(Repository, new DatabaseChangesSavingService(UnitOfWork));
    }

    // Crée le service qui renomme une catégorie.
    public CategoryRenameService CreateRename()
    {
        return new CategoryRenameService(Repository, new DatabaseChangesSavingService(UnitOfWork));
    }

    // Crée le service qui fixe le budget d'une catégorie.
    public CategoryBudgetService CreateBudget()
    {
        return new CategoryBudgetService(Repository, new DatabaseChangesSavingService(UnitOfWork));
    }

    // Crée le service qui supprime une catégorie.
    public CategoryDeletionService CreateDeletion()
    {
        return new CategoryDeletionService(Repository);
    }

    // Donne la catégorie de l'utilisateur qui porte ce nom.
    public Category Find(string name)
    {
        return Repository.SavedCategories.Single(category => category.UserId == UserId && category.Name == name);
    }

    // Donne les noms des catégories gardées pour l'utilisateur, triés.
    public List<string> ListSavedNames()
    {
        var names = Repository.SavedCategories
            .Where(category => category.UserId == UserId)
            .Select(category => category.Name)
            .ToList();
        names.Sort(StringComparer.Ordinal);

        return names;
    }

    // Donne à un autre utilisateur ses catégories par défaut, et renvoie celle qui porte ce nom.
    public Category AddOtherUserCategories(string name)
    {
        var otherCategories = DefaultCategories.CreateForUser(Guid.NewGuid());
        Repository.SavedCategories.AddRange(otherCategories);

        return otherCategories.Single(category => category.Name == name);
    }

    // Ajoute des catégories à l'utilisateur jusqu'à ce qu'il en ait ce nombre.
    public void AddCategoriesUpTo(int count)
    {
        var number = 1;

        while (Repository.SavedCategories.Count(category => category.UserId == UserId) < count)
        {
            Repository.SavedCategories.Add(new Category { Name = "Catégorie " + number, UserId = UserId });
            number = number + 1;
        }
    }

    // Ajoute une transaction de l'utilisateur classée dans cette catégorie.
    public BankTransaction AddTransaction(Category category, string label)
    {
        var transaction = new BankTransaction
        {
            ExternalTransactionId = Guid.NewGuid().ToString(),
            TransactionDate = new DateOnly(2026, 11, 10),
            Label = label,
            AmountInCents = -1500,
            BankAccountId = Guid.NewGuid(),
            CategoryId = category.Id
        };
        Repository.SavedTransactions.Add(transaction);

        return transaction;
    }
}
