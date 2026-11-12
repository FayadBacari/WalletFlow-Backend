using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Transactions;

// Accès aux opérations bancaires et aux règles de classement. Implémenté dans la couche Infrastructure.
// Les recherches d'un utilisateur reçoivent l'utilisateur de la session : l'opération d'un autre n'est jamais trouvée (SS §4).
// Les objets lus sont suivis, sauf mention contraire, et les méthodes « Prepare… » n'écrivent rien (voir IDatabaseUnitOfWork).
public interface ITransactionRepository
{
    // Donne la date de la dernière opération gardée pour ce compte, ou null s'il n'en a aucune. Rien n'est suivi.
    Task<DateOnly?> FindLatestDateAsync(Guid accountId);

    // Donne les identifiants des opérations de ce compte datées de ce jour ou après. Rien n'est suivi.
    Task<HashSet<string>> FindExternalIdsAsync(Guid accountId, DateOnly fromDate);

    // Donne les règles de classement de l'utilisateur.
    Task<List<CategorizationRule>> FindRulesAsync(Guid userId);

    // Cherche la règle de l'utilisateur pour ce libellé, sous sa forme simple. Renvoie null s'il n'en a pas.
    Task<CategorizationRule?> FindRuleAsync(Guid userId, string ruleLabel);

    // Cherche cette opération parmi celles de l'utilisateur. Renvoie null si elle n'existe pas, ou si elle est à un autre.
    Task<BankTransaction?> FindAsync(Guid userId, Guid transactionId);

    // Donne cette opération de l'utilisateur telle qu'il la voit. Renvoie null si elle n'existe pas, ou si elle est à un autre.
    // Rien n'est suivi.
    Task<TransactionView?> ReadViewAsync(Guid userId, Guid transactionId);

    // Cherche parmi les opérations de l'utilisateur, les plus récentes d'abord, et donne la page demandée. Rien n'est suivi.
    Task<TransactionPage> SearchAsync(Guid userId, TransactionSearch search);

    // Additionne les opérations de l'utilisateur datées entre ces deux jours compris, par catégorie et par mois.
    // Les transferts ne comptent pas (GR Comptes). Avec un compte, seules ses opérations comptent. Rien n'est suivi.
    Task<List<CategoryMonthTotal>> SumByCategoryAndMonthAsync(Guid userId, DateOnly from, DateOnly to, Guid? accountId);

    // Donne la date de la plus ancienne opération de l'utilisateur, ou de ce compte seul, ou null s'il n'en a aucune.
    Task<DateOnly?> FindEarliestDateAsync(Guid userId, Guid? accountId);

    // Garde de côté une nouvelle opération : elle ne sera enregistrée qu'avec les autres changements.
    void PrepareTransaction(BankTransaction transaction);

    // Garde de côté une nouvelle règle : elle ne sera enregistrée qu'avec les autres changements.
    void PrepareRule(CategorizationRule rule);
}
