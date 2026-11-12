using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Categories;

// Résultat du changement du budget d'une catégorie : toujours une seule des formes écrites dans ce fichier.
public abstract class CategoryBudgetResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private CategoryBudgetResult()
    {
    }

    // Le budget est fixé, ou enlevé.
    public sealed class Changed : CategoryBudgetResult
    {
        public required Category Category { get; init; }
    }

    // La catégorie n'existe pas, ou elle est à un autre utilisateur.
    public sealed class NotFound : CategoryBudgetResult
    {
    }

    // Le montant ne respecte pas les règles.
    public sealed class RefusedByRules : CategoryBudgetResult
    {
    }

    // D'autres requêtes changent la catégorie sans arrêt : rien n'est enregistré.
    public sealed class NotSaved : CategoryBudgetResult
    {
    }
}
