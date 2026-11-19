using WalletFlow.Application.Notifications;

namespace WalletFlow.Application.Categories;

// Cas d'utilisation : l'utilisateur connecté supprime une de ses catégories, sauf « Autres » (GR Dépenses).
// Ses transactions passent dans « Autres », ses règles disparaissent, ses suggestions sans réponse sont retirées,
// et ses suggestions acceptées restent sans catégorie, pour que les économies réalisées restent comptées (GR Sacs, MPD §1.3).
// Ses alertes partent avec elle. Les dépenses du mois d'« Autres » changent : ses seuils de budget sont vérifiés juste après.
public class CategoryDeletionService
{
    // Un interblocage veut dire qu'une autre requête a réussi à écrire : quelques essais suffisent largement.
    public const int MaximumTries = 5;

    private readonly ICategoryRepository categoryRepository;
    private readonly BudgetAlertService budgetAlerts;

    // Reçoit l'accès aux catégories et la vérification des budgets.
    public CategoryDeletionService(ICategoryRepository categoryRepository, BudgetAlertService budgetAlerts)
    {
        this.categoryRepository = categoryRepository;
        this.budgetAlerts = budgetAlerts;
    }

    // Supprime la catégorie si elle est à l'utilisateur et n'est pas « Autres ».
    public async Task<CategoryDeletionResult> DeleteAsync(Guid userId, Guid categoryId, CancellationToken cancellationToken = default)
    {
        for (var tryNumber = 1; tryNumber <= MaximumTries; tryNumber++)
        {
            var category = await categoryRepository.FindAsync(userId, categoryId, cancellationToken);

            if (category == null)
            {
                return new CategoryDeletionResult.NotFound();
            }

            if (category.IsOtherCategory)
            {
                return new CategoryDeletionResult.OtherCategoryIsFixed();
            }

            // Une autre requête peut l'avoir supprimée juste après la lecture, ou un interblocage peut avoir tout annulé :
            // la suppression ne supprime alors rien, et le passage suivant relit la catégorie.
            if (await categoryRepository.DeleteAsync(userId, categoryId, cancellationToken))
            {
                await budgetAlerts.CheckAsync(userId, cancellationToken);
                return new CategoryDeletionResult.Deleted();
            }
        }

        return new CategoryDeletionResult.NotSaved();
    }
}
