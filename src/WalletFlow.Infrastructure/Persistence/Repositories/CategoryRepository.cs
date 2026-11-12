using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Categories;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Lit et écrit les catégories dans la base PostgreSQL. Chaque requête filtre sur l'utilisateur de la session (SS §4).
// Les objets lus ici sont suivis par EF Core : l'unité de travail enregistre ce que leurs règles ont changé (voir IDatabaseUnitOfWork).
public class CategoryRepository : ICategoryRepository
{
    private readonly WalletFlowDatabaseContext databaseContext;

    // Reçoit l'accès à la base de la requête en cours.
    public CategoryRepository(WalletFlowDatabaseContext databaseContext)
    {
        this.databaseContext = databaseContext;
    }

    // Donne toutes les catégories de l'utilisateur.
    public async Task<List<Category>> FindAllAsync(Guid userId)
    {
        return await databaseContext.Categories
            .Where(category => category.UserId == userId)
            .ToListAsync();
    }

    // Cherche la catégorie par son identifiant et par son utilisateur : celle d'un autre n'est jamais trouvée.
    public async Task<Category?> FindAsync(Guid userId, Guid categoryId)
    {
        return await databaseContext.Categories.SingleOrDefaultAsync(category =>
            category.Id == categoryId
            && category.UserId == userId);
    }

    // Cherche la catégorie « Autres » de l'utilisateur : l'index ux_categorie_autres garantit qu'il n'y en a qu'une.
    public async Task<Category?> FindOtherCategoryAsync(Guid userId)
    {
        return await databaseContext.Categories.SingleOrDefaultAsync(category =>
            category.UserId == userId
            && category.IsOtherCategory);
    }

    // Compte les catégories de l'utilisateur.
    public async Task<int> CountAsync(Guid userId)
    {
        return await databaseContext.Categories.CountAsync(category =>
            category.UserId == userId);
    }

    // Compare avec lower() de PostgreSQL, comme l'index unique ux_categorie_nom : .NET ne met pas toujours les mêmes lettres
    // en minuscules que la base. La requête reste paramétrée (SS §5) : chaque valeur part comme un paramètre, jamais collée au texte.
    public async Task<bool> IsNameUsedAsync(Guid userId, string name, Guid? renamedCategoryId)
    {
        var excludedCategoryId = renamedCategoryId ?? Guid.Empty;

        return await databaseContext.Database
            .SqlQuery<bool>($"""
                SELECT EXISTS (
                    SELECT 1 FROM categorie
                    WHERE id_utilisateur = {userId}
                      AND id_categorie <> {excludedCategoryId}
                      AND lower(nom_categorie) = lower({name})) AS "Value"
                """)
            .SingleAsync();
    }

    // Garde de côté la nouvelle catégorie : elle ne sera écrite qu'avec les autres changements.
    public void PrepareCategory(Category category)
    {
        databaseContext.Categories.Add(category);
    }

    // La catégorie sera écrite même si rien n'y a changé : cette écriture contrôle en base la version de la ligne,
    // et elle échoue si une autre requête a écrit la catégorie depuis sa lecture. EF Core n'écrit une ligne que si une colonne
    // a changé : le nom est donc réécrit avec sa propre valeur.
    public void PrepareUnchangedCheck(Category category)
    {
        databaseContext.Entry(category).Property(nameof(Category.Name)).IsModified = true;
    }

    // Les deux écritures forment une seule transaction SQL. Cette écriture est immédiate : elle refuse de partir si des changements
    // attendent l'unité de travail, pour ne jamais les emporter avec elle.
    public async Task<bool> DeleteAsync(Guid userId, Guid categoryId)
    {
        databaseContext.ThrowIfChangesArePending();

        // Une transaction déjà ouverte par l'appelant couvre les deux écritures : EF Core refuse d'en ouvrir une seconde.
        if (databaseContext.Database.CurrentTransaction != null)
        {
            return await DeleteThenMoveTransactionsAsync(userId, categoryId);
        }

        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var isDeleted = await DeleteThenMoveTransactionsAsync(userId, categoryId);
        await transaction.CommitAsync();

        return isDeleted;
    }

    // Supprime d'abord la catégorie : sa ligne reste verrouillée jusqu'à la fin de la transaction, et une transaction bancaire
    // ajoutée au même instant ne peut plus la viser. Puis déplace dans « Autres » les transactions qui la visaient.
    // La clé étrangère est différée (MPD §1.3) : elle n'est vérifiée qu'à la fin, quand tout est déjà dans « Autres ».
    // Les règles de la catégorie disparaissent avec elle (ON DELETE CASCADE), ses suggestions restent sans catégorie (SET NULL).
    private async Task<bool> DeleteThenMoveTransactionsAsync(Guid userId, Guid categoryId)
    {
        var deletedCount = await databaseContext.Categories
            .Where(category =>
                category.Id == categoryId
                && category.UserId == userId
                && !category.IsOtherCategory)
            .ExecuteDeleteAsync();

        if (deletedCount == 0)
        {
            return false;
        }

        var otherCategoryId = await databaseContext.Categories
            .Where(category => category.UserId == userId && category.IsOtherCategory)
            .Select(category => category.Id)
            .SingleAsync();

        await databaseContext.BankTransactions
            .Where(transaction => transaction.CategoryId == categoryId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(transaction => transaction.CategoryId, otherCategoryId));

        return true;
    }
}
