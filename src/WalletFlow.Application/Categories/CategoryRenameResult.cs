using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Categories;

// Résultat du changement de nom d'une catégorie : toujours une seule des formes écrites dans ce fichier.
public abstract class CategoryRenameResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private CategoryRenameResult()
    {
    }

    // La catégorie porte son nouveau nom.
    public sealed class Renamed : CategoryRenameResult
    {
        public required Category Category { get; init; }
    }

    // La catégorie n'existe pas, ou elle est à un autre utilisateur.
    public sealed class NotFound : CategoryRenameResult
    {
    }

    // C'est « Autres », qui ne se renomme pas.
    public sealed class OtherCategoryIsFixed : CategoryRenameResult
    {
    }

    // Le nom ne respecte pas les règles.
    public sealed class RefusedByRules : CategoryRenameResult
    {
    }

    // Une autre catégorie de l'utilisateur porte déjà ce nom, majuscules ignorées.
    public sealed class NameAlreadyUsed : CategoryRenameResult
    {
    }

    // D'autres requêtes changent les catégories sans arrêt : rien n'est enregistré.
    public sealed class NotSaved : CategoryRenameResult
    {
    }
}
