using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Suggestions;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Lit et écrit les suggestions d'épargne dans la base PostgreSQL. Chaque requête filtre sur l'utilisateur de la session (SS §4).
// Les objets lus ici sont suivis par EF Core : l'unité de travail enregistre ce que leurs règles ont changé (voir IDatabaseUnitOfWork).
public class SuggestionRepository : ISuggestionRepository
{
    private readonly WalletFlowDatabaseContext databaseContext;

    // Reçoit l'accès à la base de la requête en cours.
    public SuggestionRepository(WalletFlowDatabaseContext databaseContext)
    {
        this.databaseContext = databaseContext;
    }

    // Donne toutes les suggestions de l'utilisateur. Elles passent par l'index ix_suggestion_utilisateur.
    public async Task<List<SavingsSuggestion>> FindAllAsync(Guid userId)
    {
        return await databaseContext.SavingsSuggestions
            .Where(suggestion => suggestion.UserId == userId)
            .ToListAsync();
    }

    // Cherche la suggestion par son identifiant et par son utilisateur : celle d'un autre n'est jamais trouvée.
    public async Task<SavingsSuggestion?> FindAsync(Guid userId, Guid suggestionId)
    {
        return await databaseContext.SavingsSuggestions.SingleOrDefaultAsync(suggestion =>
            suggestion.Id == suggestionId
            && suggestion.UserId == userId);
    }

    // Cherche les utilisateurs en SQL, par paquets, dans l'ordre de leur identifiant : un utilisateur dont le calcul échoue
    // ne bloque pas les suivants, puisque le paquet suivant commence après lui. Une opération existe dès qu'un compte d'une
    // de ses connexions en a une : chaque EXISTS s'arrête à la première ligne trouvée par l'index de sa table.
    public async Task<List<Guid>> FindUsersToCalculateAsync(DateOnly month, Guid? afterUserId, int count)
    {
        var users = databaseContext.Users
            .AsNoTracking()
            .Where(user =>
                user.IsEmailConfirmed
                && user.DeletionRequestedAt == null
                && (user.LastSuggestionMonth == null || user.LastSuggestionMonth < month)
                && databaseContext.BankConnections.Any(connection =>
                    connection.UserId == user.Id
                    && databaseContext.BankAccounts.Any(account =>
                        account.BankConnectionId == connection.Id
                        && databaseContext.BankTransactions.Any(transaction => transaction.BankAccountId == account.Id))));

        if (afterUserId is Guid lastUserId)
        {
            users = users.Where(user => user.Id.CompareTo(lastUserId) > 0);
        }

        return await users
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .Take(count)
            .ToListAsync();
    }

    // Additionne en SQL, en bigint, les mouvements liés aux suggestions de l'utilisateur. Seule une suggestion acceptée crée
    // un mouvement. Les suggestions de l'utilisateur sont lues d'abord : la somme passe alors par l'index unique du lien
    // id_suggestion, sans parcourir les mouvements de tous les utilisateurs.
    public async Task<long> SumSavedAsync(Guid userId)
    {
        var suggestionIds = await databaseContext.SavingsSuggestions
            .Where(suggestion => suggestion.UserId == userId && suggestion.Status == SuggestionStatus.Accepted)
            .Select(suggestion => suggestion.Id)
            .ToListAsync();

        return await databaseContext.BagMovements
            .Where(movement =>
                movement.SavingsSuggestionId != null
                && suggestionIds.Contains(movement.SavingsSuggestionId.Value))
            .SumAsync(movement => (long)movement.AmountInCents);
    }

    // Garde de côté la nouvelle suggestion : elle ne sera écrite qu'avec les autres changements.
    public void PrepareSuggestion(SavingsSuggestion suggestion)
    {
        databaseContext.SavingsSuggestions.Add(suggestion);
    }

    // Prépare l'effacement de la suggestion. La base garde le mouvement qu'elle a créé, sans lien (ON DELETE SET NULL).
    public void PrepareDeletion(SavingsSuggestion suggestion)
    {
        databaseContext.SavingsSuggestions.Remove(suggestion);
    }
}
