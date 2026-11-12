using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Categories;

// Résultat de la création d'une catégorie : toujours une seule des formes écrites dans ce fichier.
public abstract class CategoryCreationResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private CategoryCreationResult()
    {
    }

    // La catégorie est créée.
    public sealed class Created : CategoryCreationResult
    {
        public required Category Category { get; init; }
    }

    // Le nom ne respecte pas les règles.
    public sealed class RefusedByRules : CategoryCreationResult
    {
    }

    // L'utilisateur a déjà une catégorie de ce nom, majuscules ignorées.
    public sealed class NameAlreadyUsed : CategoryCreationResult
    {
    }

    // L'utilisateur a déjà le nombre maximum de catégories.
    public sealed class TooManyCategories : CategoryCreationResult
    {
    }

    // D'autres requêtes changent les catégories sans arrêt, ou le compte n'existe plus : rien n'est enregistré.
    public sealed class NotSaved : CategoryCreationResult
    {
    }
}
