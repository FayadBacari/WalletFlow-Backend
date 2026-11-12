using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WalletFlow.Application.Categories;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Courses entre requêtes simultanées sur les catégories, avec la vraie API et la vraie base de test :
// chaque requête a son propre accès à la base et sa propre transaction, comme dans l'API.
[Collection(RealDatabaseCollection.Name)]
public class ConcurrentCategoryRequestsTests
{
    private const int SimultaneousRequestCount = 20;
    private const int MaximumImportsPerTask = 1000;
    private const string Added = "ajoutée";
    private const string RefusedByTheForeignKey = "refusée par la clé étrangère";
    private const string Deleted = "supprimée";
    private const string NotDeleted = "pas supprimée";

    // Enregistre pour de vrai un compte confirmé avec ses 8 catégories par défaut.
    private static async Task<User> SaveAccountWithCategoriesAsync(string email)
    {
        var account = new User { Email = email, PasswordHash = "empreinte-de-test", IsEmailConfirmed = true };

        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.Users.Add(account);
        databaseContext.Categories.AddRange(DefaultCategories.CreateForUser(account.Id));
        await databaseContext.SaveChangesAsync();

        return account;
    }

    // Enregistre pour de vrai ce nombre de catégories de plus pour ce compte.
    private static async Task AddCategoriesAsync(Guid userId, int count)
    {
        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();

        for (var number = 1; number <= count; number++)
        {
            databaseContext.Categories.Add(new Category { Name = "Catégorie " + number, UserId = userId });
        }

        await databaseContext.SaveChangesAsync();
    }

    // Enregistre pour de vrai une catégorie de ce compte, et la renvoie.
    private static async Task<Category> SaveCategoryAsync(Guid userId, string name)
    {
        var category = new Category { Name = name, UserId = userId };

        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.Categories.Add(category);
        await databaseContext.SaveChangesAsync();

        return category;
    }

    // Enregistre pour de vrai une connexion et un compte bancaires pour ce compte, et renvoie l'identifiant du compte bancaire.
    private static async Task<Guid> SaveBankAccountAsync(Guid userId)
    {
        var connection = new BankConnection
        {
            ExternalConnectionId = "connexion-" + Guid.NewGuid(),
            BankName = "Banque de test",
            AuthorizationExpiresAt = DateTime.UtcNow.AddDays(180),
            UserId = userId
        };
        var bankAccount = new BankAccount
        {
            ExternalAccountId = "compte-" + Guid.NewGuid(),
            Name = "Compte courant",
            BalanceInCents = 100000,
            BalanceUpdatedAt = DateTime.UtcNow,
            BankConnectionId = connection.Id
        };

        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.AddRange(connection, bankAccount);
        await databaseContext.SaveChangesAsync();

        return bankAccount.Id;
    }

    // Supprime la catégorie avec le vrai cas d'utilisation, et dit ce qui s'est passé.
    private static async Task<string> DeleteCategoryAsync(IServiceProvider services, Guid userId, Guid categoryId)
    {
        var result = await services.GetRequiredService<CategoryDeletionService>().DeleteAsync(userId, categoryId);

        return result is CategoryDeletionResult.Deleted ? Deleted : NotDeleted;
    }

    // Ajoute une transaction dans cette catégorie avec son propre accès à la base, comme un import de la banque,
    // et dit ce qui s'est passé. Seul un refus de la clé étrangère est attendu : toute autre erreur fait échouer le test.
    private static async Task<string> AddTransactionAsync(Guid bankAccountId, Guid categoryId)
    {
        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.BankTransactions.Add(new BankTransaction
        {
            ExternalTransactionId = "operation-" + Guid.NewGuid(),
            TransactionDate = new DateOnly(2026, 11, 12),
            Label = "IMPORT",
            AmountInCents = -500,
            BankAccountId = bankAccountId,
            CategoryId = categoryId
        });

        try
        {
            await databaseContext.SaveChangesAsync();
            return Added;
        }
        catch (Exception exception) when (IsForeignKeyViolation(exception))
        {
            return RefusedByTheForeignKey;
        }
    }

    // Renvoie vrai si l'erreur vient d'une clé étrangère : la catégorie visée n'existe plus.
    private static bool IsForeignKeyViolation(Exception exception)
    {
        var postgresException = exception as PostgresException ?? exception.InnerException as PostgresException;

        return postgresException?.SqlState == PostgresErrorCodes.ForeignKeyViolation;
    }

    // SS §6 : 20 créations en même temps, à 95 catégories. Exactement 5 passent, et le compte finit avec 100 catégories.
    // Sans la réécriture de « Autres », chaque requête compterait 95, et les 20 passeraient.
    [Fact]
    public async Task SimultaneousCreationsNeverGoBeyondTheLimit()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-limite-categories");

        try
        {
            var account = await SaveAccountWithCategoriesAsync(email);
            await AddCategoriesAsync(account.Id, 87);

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, requestNumber) =>
                services.GetRequiredService<CategoryCreationService>().CreateAsync(account.Id, "Course " + requestNumber));

            Assert.Equal(5, results.Count(result => result is CategoryCreationResult.Created));
            Assert.Equal(15, results.Count(result => result is CategoryCreationResult.TooManyCategories));
            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            Assert.Equal(100, await databaseContext.Categories.CountAsync(row => row.UserId == account.Id));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // DD §6.1 : 20 créations du même nom en même temps, en minuscules et en majuscules. Une seule passe,
    // les autres voient ensuite que le nom est pris : l'index unique en fait des conflits, pas des pannes.
    [Fact]
    public async Task SimultaneousCreationsOfTheSameNameCreateItOnce()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-nom-categorie");

        try
        {
            var account = await SaveAccountWithCategoriesAsync(email);

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, requestNumber) =>
                services.GetRequiredService<CategoryCreationService>().CreateAsync(
                    account.Id,
                    requestNumber % 2 == 0 ? "Abonnements" : "ABONNEMENTS"));

            Assert.Equal(1, results.Count(result => result is CategoryCreationResult.Created));
            Assert.Equal(SimultaneousRequestCount - 1, results.Count(result => result is CategoryCreationResult.NameAlreadyUsed));
            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            Assert.Equal(1, await databaseContext.Categories.CountAsync(row =>
                row.UserId == account.Id && row.Name.ToLower() == "abonnements"));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // Ajoute des transactions dans cette catégorie, l'une après l'autre, comme un import de la banque, jusqu'à ce que
    // la clé étrangère en refuse une : la catégorie a alors disparu. Renvoie le nombre de transactions ajoutées avant.
    private static async Task<int> ImportUntilRefusedAsync(Guid bankAccountId, Guid categoryId)
    {
        for (var addedCount = 0; addedCount < MaximumImportsPerTask; addedCount++)
        {
            if (await AddTransactionAsync(bankAccountId, categoryId) == RefusedByTheForeignKey)
            {
                return addedCount;
            }
        }

        throw new InvalidOperationException("La catégorie n'a jamais disparu : la suppression n'a pas eu lieu.");
    }

    // GR Dépenses et MPD §1.3 : une catégorie est supprimée pendant que 20 imports y ajoutent des transactions sans arrêt.
    // La suppression réussit toujours, aucune transaction ne reste dans la catégorie supprimée, et chaque transaction ajoutée
    // est rangée dans « Autres ». La course joue à coup sûr dans les deux sens : une transaction est là avant la suppression,
    // et chaque import continue jusqu'à ce que la clé étrangère le refuse, donc après la suppression. Trois manches.
    [Fact]
    public async Task DeletionWhileTransactionsArriveLeavesNoTransactionBehind()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-suppression-categorie");

        try
        {
            var account = await SaveAccountWithCategoriesAsync(email);
            var bankAccountId = await SaveBankAccountAsync(account.Id);
            var addedCount = 0;

            for (var round = 1; round <= 3; round++)
            {
                var category = await SaveCategoryAsync(account.Id, "Manche " + round);
                Assert.Equal(Added, await AddTransactionAsync(bankAccountId, category.Id));
                addedCount = addedCount + 1;

                var startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var imports = new List<Task<int>>();
                for (var importNumber = 0; importNumber < SimultaneousRequestCount; importNumber++)
                {
                    imports.Add(Task.Run(async () =>
                    {
                        await startSignal.Task;
                        return await ImportUntilRefusedAsync(bankAccountId, category.Id);
                    }));
                }
                var deletion = Task.Run(async () =>
                {
                    await startSignal.Task;
                    await using var scope = api.Services.CreateAsyncScope();
                    return await DeleteCategoryAsync(scope.ServiceProvider, account.Id, category.Id);
                });

                startSignal.SetResult();
                Assert.Equal(Deleted, await deletion);
                addedCount = addedCount + (await Task.WhenAll(imports)).Sum();

                await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
                var otherCategory = await databaseContext.Categories.AsNoTracking().SingleAsync(row => row.UserId == account.Id && row.IsOtherCategory);
                Assert.Equal(0, await databaseContext.BankTransactions.CountAsync(row => row.CategoryId == category.Id));
                Assert.Equal(addedCount, await databaseContext.BankTransactions.CountAsync(row => row.CategoryId == otherCategory.Id));
            }
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }
}
