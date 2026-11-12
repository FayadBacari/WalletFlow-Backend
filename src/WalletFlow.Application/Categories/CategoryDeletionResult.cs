namespace WalletFlow.Application.Categories;

// Résultat de la suppression d'une catégorie : toujours une seule des formes écrites dans ce fichier.
public abstract class CategoryDeletionResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private CategoryDeletionResult()
    {
    }

    // La catégorie est supprimée, et ses transactions sont dans « Autres ».
    public sealed class Deleted : CategoryDeletionResult
    {
    }

    // La catégorie n'existe pas, ou plus, ou elle est à un autre utilisateur.
    public sealed class NotFound : CategoryDeletionResult
    {
    }

    // C'est « Autres », qui ne se supprime pas.
    public sealed class OtherCategoryIsFixed : CategoryDeletionResult
    {
    }
}
