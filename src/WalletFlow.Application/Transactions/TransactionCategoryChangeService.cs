using WalletFlow.Application.Categories;
using WalletFlow.Application.Persistence;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Transactions;

// Cas d'utilisation : l'utilisateur connecté change la catégorie d'une opération (GR Dépenses). S'il le demande,
// ce choix s'appliquera aussi aux prochaines opérations qui ont le même libellé : une règle de l'utilisateur est créée,
// ou la sienne pour ce libellé change de catégorie. Les opérations déjà reçues ne changent pas.
public class TransactionCategoryChangeService
{
    private readonly ITransactionRepository transactionRepository;
    private readonly ICategoryRepository categoryRepository;
    private readonly DatabaseChangesSavingService savingService;

    // Reçoit l'accès aux opérations et aux catégories, et l'enregistrement avec réessai.
    public TransactionCategoryChangeService(
        ITransactionRepository transactionRepository,
        ICategoryRepository categoryRepository,
        DatabaseChangesSavingService savingService)
    {
        this.transactionRepository = transactionRepository;
        this.categoryRepository = categoryRepository;
        this.savingService = savingService;
    }

    // Range l'opération dans cette catégorie, crée ou change la règle de son libellé si l'utilisateur le demande,
    // puis relit l'opération telle qu'il la voit maintenant.
    public async Task<TransactionCategoryChangeResult> ChangeAsync(Guid userId, Guid transactionId, Guid categoryId, bool applyToFutureTransactions)
    {
        var refusal = await savingService.SaveWithRetriesAsync(
            () => PrepareChangeAsync(userId, transactionId, categoryId, applyToFutureTransactions),
            (TransactionCategoryChangeResult?)new TransactionCategoryChangeResult.NotSaved());

        if (refusal != null)
        {
            return refusal;
        }

        var view = await transactionRepository.ReadViewAsync(userId, transactionId);

        // La banque vient d'être déconnectée, avec ses opérations.
        if (view == null)
        {
            return new TransactionCategoryChangeResult.TransactionNotFound();
        }

        return new TransactionCategoryChangeResult.Changed { Transaction = view };
    }

    // Un passage : vérifie l'opération et la catégorie, puis prépare le changement et la règle. Renvoie le refus,
    // ou null quand le changement est prêt. « Autres » est réécrite avec une règle : de deux règles créées au même instant
    // pour un même libellé, la seconde échoue, relit, et change la première au lieu d'en créer une autre.
    // Il reste ainsi une seule règle par libellé.
    private async Task<PreparedChanges<TransactionCategoryChangeResult?>> PrepareChangeAsync(
        Guid userId,
        Guid transactionId,
        Guid categoryId,
        bool applyToFutureTransactions)
    {
        var transaction = await transactionRepository.FindAsync(userId, transactionId);

        if (transaction == null)
        {
            return PreparedChanges<TransactionCategoryChangeResult?>.NothingToSave(new TransactionCategoryChangeResult.TransactionNotFound());
        }

        var category = await categoryRepository.FindAsync(userId, categoryId);

        if (category == null)
        {
            return PreparedChanges<TransactionCategoryChangeResult?>.NothingToSave(new TransactionCategoryChangeResult.CategoryNotFound());
        }

        var ruleLabel = TransactionLabels.Simplify(transaction.Label);

        if (applyToFutureTransactions && !CategorizationRule.IsLabelValid(ruleLabel))
        {
            return PreparedChanges<TransactionCategoryChangeResult?>.NothingToSave(new TransactionCategoryChangeResult.LabelWithoutLetter());
        }

        transaction.ChangeCategory(category.Id);

        if (applyToFutureTransactions)
        {
            var otherCategory = await categoryRepository.FindOtherCategoryAsync(userId);

            if (otherCategory == null)
            {
                return PreparedChanges<TransactionCategoryChangeResult?>.NothingToSave(new TransactionCategoryChangeResult.NotSaved());
            }

            var rule = await transactionRepository.FindRuleAsync(userId, ruleLabel);

            if (rule == null)
            {
                transactionRepository.PrepareRule(new CategorizationRule { Label = ruleLabel, CategoryId = category.Id });
            }
            else
            {
                rule.ChangeCategory(category.Id);
            }

            categoryRepository.PrepareUnchangedCheck(otherCategory);
        }

        return PreparedChanges<TransactionCategoryChangeResult?>.SaveThenReturn(null);
    }
}
