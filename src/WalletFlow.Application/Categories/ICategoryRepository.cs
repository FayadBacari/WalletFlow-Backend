using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Categories;

// Accès aux catégories des utilisateurs. Implémenté dans la couche Infrastructure.
// Chaque recherche reçoit l'utilisateur de la session : la catégorie d'un autre n'est jamais trouvée (SS §4).
// Les objets lus sont suivis, et les méthodes « Prepare… » n'écrivent rien (voir IDatabaseUnitOfWork).
public interface ICategoryRepository
{
    // Donne toutes les catégories de l'utilisateur, dans aucun ordre particulier.
    Task<List<Category>> FindAllAsync(Guid userId);

    // Cherche cette catégorie parmi celles de l'utilisateur. Renvoie null si elle n'existe pas, ou si elle est à un autre.
    Task<Category?> FindAsync(Guid userId, Guid categoryId);

    // Cherche la catégorie « Autres » de l'utilisateur. Renvoie null si son compte n'existe plus.
    Task<Category?> FindOtherCategoryAsync(Guid userId);

    // Compte les catégories de l'utilisateur.
    Task<int> CountAsync(Guid userId);

    // Renvoie vrai si une autre catégorie de l'utilisateur porte déjà ce nom, majuscules ignorées, comme le compare la base.
    // La catégorie donnée, celle qu'on renomme, n'est pas comptée.
    Task<bool> IsNameUsedAsync(Guid userId, string name, Guid? renamedCategoryId);

    // Garde de côté une nouvelle catégorie : elle ne sera enregistrée qu'avec les autres changements.
    void PrepareCategory(Category category);

    // La catégorie sera écrite même si rien n'y a changé : l'enregistrement échoue alors si une autre requête l'a écrite
    // depuis sa lecture. Sur « Autres », cela empêche deux créations simultanées de dépasser ensemble le nombre maximum.
    void PrepareUnchangedCheck(Category category);

    // Supprime la catégorie de l'utilisateur et déplace ses transactions dans sa catégorie « Autres », en une seule transaction SQL.
    // Cette écriture est immédiate. Renvoie faux, sans rien changer, si la catégorie n'existe plus, est à un autre, ou est « Autres ».
    Task<bool> DeleteAsync(Guid userId, Guid categoryId);
}
