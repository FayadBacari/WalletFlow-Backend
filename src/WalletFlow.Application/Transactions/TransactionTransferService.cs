using WalletFlow.Application.Notifications;
using WalletFlow.Application.Persistence;

namespace WalletFlow.Application.Transactions;

// Cas d'utilisation : l'utilisateur connecté marque une opération comme transfert entre deux de ses comptes, ou retire
// cette marque (GR Comptes). Un transfert n'est compté ni en dépense ni en revenu. Seule l'opération choisie change :
// l'opération en face, sur son autre compte, l'utilisateur la marque lui aussi. Les dépenses du mois de sa catégorie changent :
// ses seuils de budget sont vérifiés juste après.
public class TransactionTransferService
{
    private readonly ITransactionRepository transactionRepository;
    private readonly BudgetAlertService budgetAlerts;
    private readonly DatabaseChangesSavingService savingService;

    // Reçoit l'accès aux opérations, la vérification des budgets et l'enregistrement avec réessai.
    public TransactionTransferService(
        ITransactionRepository transactionRepository,
        BudgetAlertService budgetAlerts,
        DatabaseChangesSavingService savingService)
    {
        this.transactionRepository = transactionRepository;
        this.budgetAlerts = budgetAlerts;
        this.savingService = savingService;
    }

    // Donne à l'opération la marque demandée, transfert ou non, puis la relit telle que l'utilisateur la voit maintenant.
    public async Task<TransactionTransferResult> MarkAsync(Guid userId, Guid transactionId, bool isInternalTransfer)
    {
        var refusal = await savingService.SaveWithRetriesAsync(
            () => PrepareMarkAsync(userId, transactionId, isInternalTransfer),
            (TransactionTransferResult?)new TransactionTransferResult.NotSaved());

        if (refusal != null)
        {
            return refusal;
        }

        await budgetAlerts.CheckAsync(userId);

        var view = await transactionRepository.ReadViewAsync(userId, transactionId);

        // La banque vient d'être déconnectée, avec ses opérations.
        if (view == null)
        {
            return new TransactionTransferResult.NotFound();
        }

        return new TransactionTransferResult.Marked { Transaction = view };
    }

    // Un passage : vérifie l'opération, puis prépare sa marque. Renvoie le refus, ou null quand la marque est prête.
    private async Task<PreparedChanges<TransactionTransferResult?>> PrepareMarkAsync(Guid userId, Guid transactionId, bool isInternalTransfer)
    {
        var transaction = await transactionRepository.FindAsync(userId, transactionId);

        if (transaction == null)
        {
            return PreparedChanges<TransactionTransferResult?>.NothingToSave(new TransactionTransferResult.NotFound());
        }

        transaction.MarkAsInternalTransfer(isInternalTransfer);

        return PreparedChanges<TransactionTransferResult?>.SaveThenReturn(null);
    }
}
