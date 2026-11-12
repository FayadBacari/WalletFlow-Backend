using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Categories;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Courses entre requêtes simultanées sur les opérations, avec la vraie API et la vraie base de test : chaque requête a son
// propre accès à la base et sa propre transaction, comme dans l'API. Seul le prestataire est faux.
[Collection(RealDatabaseCollection.Name)]
public class ConcurrentTransactionRequestsTests
{
    private const int SimultaneousRequestCount = 20;
    private const string StableId = "stable-courses";
    private static readonly UserPresence Presence = new UserPresence(IPAddress.Loopback, "Course");

    // Enregistre pour de vrai un compte confirmé, avec ses 8 catégories par défaut.
    private static async Task<User> SaveAccountAsync(string email)
    {
        var account = new User { Email = email, PasswordHash = "empreinte-de-test", IsEmailConfirmed = true };

        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.Users.Add(account);
        databaseContext.Categories.AddRange(DefaultCategories.CreateForUser(account.Id));
        await databaseContext.SaveChangesAsync();

        return account;
    }

    // Connecte la banque des tests avec le vrai cas d'utilisation, et renvoie l'identifiant de la connexion.
    private static async Task<Guid> ConnectAsync(ApiFactoryWithRealDatabase api, Guid userId)
    {
        await using var startScope = api.Services.CreateAsyncScope();
        var start = await startScope.ServiceProvider.GetRequiredService<BankAuthorizationStartService>().StartNewAsync(userId, FakeBankDataProvider.BankName);
        Assert.IsType<BankAuthorizationStartResult.Started>(start);
        api.BankProvider.AddSession("code-courses", new TestBankAccount(StableId, "Compte courant", "EUR", 10_000));

        await using var finishScope = api.Services.CreateAsyncScope();
        var result = await finishScope.ServiceProvider.GetRequiredService<BankConnectionFinishService>()
            .FinishAsync(userId, "code-courses", api.BankProvider.AuthorizationRequests.Last().State, Presence);

        return Assert.IsType<BankConnectionFinishResult.Connected>(result).ConnectionId;
    }

    // Ajoute chez le faux prestataire ce nombre d'opérations nouvelles, toutes datées d'aujourd'hui.
    private static void AddOperations(ApiFactoryWithRealDatabase api, int count, string labelStart)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        for (var number = 0; number < count; number++)
        {
            api.BankProvider.AddTransaction(StableId, today, -100 - number, labelStart + " " + number);
        }
    }

    // Donne les opérations gardées pour le compte de cette connexion.
    private static async Task<List<BankTransaction>> ReadOperationsAsync(Guid connectionId)
    {
        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        var accountId = await databaseContext.BankAccounts.AsNoTracking()
            .Where(account => account.BankConnectionId == connectionId)
            .Select(account => account.Id)
            .SingleAsync();

        return await databaseContext.BankTransactions.AsNoTracking().Where(transaction => transaction.BankAccountId == accountId).ToListAsync();
    }

    // DD §5.3 : 20 mises à jour en même temps reçoivent les mêmes opérations. Chacune n'est gardée qu'une fois,
    // et aucune mise à jour n'échoue : la contrainte d'unicité devient un conflit, et le passage suivant relit tout.
    [Fact]
    public async Task SimultaneousUpdatesKeepEachOperationOnce()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-operations");

        try
        {
            var user = await SaveAccountAsync(email);
            var connectionId = await ConnectAsync(api, user.Id);

            for (var round = 1; round <= 3; round++)
            {
                AddOperations(api, 15, "CB CAFE TOUR " + round);

                await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, async (services, requestNumber) =>
                {
                    await services.GetRequiredService<BankBalanceUpdateService>().UpdateConnectionAsync(connectionId, null);
                    return true;
                });

                var operations = await ReadOperationsAsync(connectionId);
                Assert.Equal(15 * round, operations.Count);
                Assert.Equal(15 * round, operations.Select(operation => operation.ExternalTransactionId).Distinct().Count());
            }
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // GR Dépenses : 20 choix simultanés pour le même libellé, vers des catégories différentes : il reste une seule règle,
    // celle d'un choix enregistré. « Autres », réécrite à chaque règle, fait passer les choix l'un après l'autre.
    [Fact]
    public async Task SimultaneousRulesForTheSameLabelLeaveOne()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-regles");

        try
        {
            var user = await SaveAccountAsync(email);
            AddOperations(api, SimultaneousRequestCount, "CB CARREFOUR");
            var connectionId = await ConnectAsync(api, user.Id);
            var operations = await ReadOperationsAsync(connectionId);
            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            var categories = await databaseContext.Categories.AsNoTracking()
                .Where(category => category.UserId == user.Id && !category.IsOtherCategory)
                .ToListAsync();

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, requestNumber) =>
                services.GetRequiredService<TransactionCategoryChangeService>().ChangeAsync(
                    user.Id,
                    operations[requestNumber].Id,
                    categories[requestNumber % categories.Count].Id,
                    true));

            var rules = await databaseContext.CategorizationRules.AsNoTracking()
                .Where(rule => rule.Label == "CB CARREFOUR" && categories.Select(category => category.Id).Contains(rule.CategoryId))
                .ToListAsync();
            var savedCategoryIds = results
                .OfType<TransactionCategoryChangeResult.Changed>()
                .Select(changed => changed.Transaction.CategoryId)
                .ToList();
            Assert.Single(rules);
            Assert.Contains(rules[0].CategoryId, savedCategoryIds);
            Assert.All(results, result => Assert.True(
                result is TransactionCategoryChangeResult.Changed || result is TransactionCategoryChangeResult.NotSaved));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // Une catégorie supprimée pendant que des opérations arrivent pour elle : chaque opération est gardée une fois,
    // dans une catégorie qui existe. Celles qui arrivent après la suppression vont dans « Autres », sans panne.
    [Fact]
    public async Task CategoryDeletedWhileOperationsArriveLeavesNoOperationBehind()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-suppression-categorie");

        try
        {
            var user = await SaveAccountAsync(email);
            var connectionId = await ConnectAsync(api, user.Id);
            Guid foodId;

            await using (var databaseContext = LocalTestDatabase.CreateDatabaseContext())
            {
                foodId = await databaseContext.Categories.AsNoTracking()
                    .Where(category => category.UserId == user.Id && category.Name == "Alimentation")
                    .Select(category => category.Id)
                    .SingleAsync();
            }

            AddOperations(api, 30, "CB CARREFOUR");

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, async (services, requestNumber) =>
            {
                if (requestNumber == SimultaneousRequestCount / 2)
                {
                    return await services.GetRequiredService<CategoryDeletionService>().DeleteAsync(user.Id, foodId) is CategoryDeletionResult.Deleted;
                }

                await services.GetRequiredService<BankBalanceUpdateService>().UpdateConnectionAsync(connectionId, null);
                return true;
            });

            var operations = await ReadOperationsAsync(connectionId);
            await using var checkContext = LocalTestDatabase.CreateDatabaseContext();
            var existingCategoryIds = await checkContext.Categories.AsNoTracking()
                .Where(category => category.UserId == user.Id)
                .Select(category => category.Id)
                .ToListAsync();
            Assert.True(results[SimultaneousRequestCount / 2]);
            Assert.Equal(30, operations.Count);
            Assert.All(operations, operation => Assert.Contains(operation.CategoryId, existingCategoryIds));
            Assert.DoesNotContain(foodId, existingCategoryIds);
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }
}
