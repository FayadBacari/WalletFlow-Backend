using Microsoft.EntityFrameworkCore;
using Npgsql;
using WalletFlow.Application.Categories;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les catégories dans la vraie base PostgreSQL locale, avec l'utilisateur de l'API (GR Dépenses, SS §4 et §6, MPD).
// Chaque test travaille dans une transaction annulée à la fin : la base reste vide.
public class CategoryRepositoryTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    // Enregistre un compte confirmé avec ses 8 catégories par défaut, puis oublie les objets gardés en mémoire.
    private static async Task<User> SaveAccountWithCategoriesAsync(WalletFlowDatabaseContext databaseContext, string email)
    {
        var account = await SaveConfirmedAccountAsync(databaseContext, email);
        databaseContext.Categories.AddRange(DefaultCategories.CreateForUser(account.Id));
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        return account;
    }

    // Lit, sans la suivre, la catégorie de ce compte qui porte ce nom.
    private static async Task<Category> ReadCategoryAsync(WalletFlowDatabaseContext databaseContext, Guid userId, string name)
    {
        return await databaseContext.Categories.AsNoTracking().SingleAsync(row => row.UserId == userId && row.Name == name);
    }

    // Enregistre une transaction de ce compte classée dans cette catégorie, avec sa connexion et son compte bancaires.
    private static async Task<BankTransaction> SaveTransactionAsync(WalletFlowDatabaseContext databaseContext, User account, Category category, string label)
    {
        var connection = new BankConnection
        {
            ExternalConnectionId = "connexion-" + Guid.NewGuid(),
            BankName = "Banque de test",
            AuthorizationExpiresAt = Now.AddDays(180),
            UserId = account.Id
        };
        var bankAccount = new BankAccount
        {
            ExternalAccountId = "compte-" + Guid.NewGuid(),
            Name = "Compte courant",
            BalanceInCents = 100000,
            BalanceUpdatedAt = Now,
            BankConnectionId = connection.Id
        };
        var transaction = new BankTransaction
        {
            ExternalTransactionId = "operation-" + Guid.NewGuid(),
            TransactionDate = new DateOnly(2026, 11, 10),
            Label = label,
            AmountInCents = -1200,
            BankAccountId = bankAccount.Id,
            CategoryId = category.Id
        };
        databaseContext.AddRange(connection, bankAccount, transaction);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        return transaction;
    }

    // Essaie d'écrire ce nom pour ce compte, dans un point de sauvegarde annulé aussitôt : renvoie vrai si l'index unique le refuse.
    private static async Task<bool> IsRefusedByTheUniqueIndexAsync(WalletFlowDatabaseContext databaseContext, Guid userId, string name)
    {
        await databaseContext.Database.ExecuteSqlRawAsync("SAVEPOINT essai_index");

        try
        {
            await databaseContext.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO categorie (nom_categorie, id_utilisateur) VALUES ({name}, {userId})");
            return false;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return true;
        }
        finally
        {
            await databaseContext.Database.ExecuteSqlRawAsync("ROLLBACK TO SAVEPOINT essai_index");
        }
    }

    // SS §4 : chaque recherche filtre sur l'utilisateur de la session.
    [Fact]
    public async Task CategoryOfAnotherUserIsNeverFound()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountWithCategoriesAsync(databaseContext, "test-categories-a@exemple.test");
        var otherAccount = await SaveAccountWithCategoriesAsync(databaseContext, "test-categories-b@exemple.test");
        var otherLeisure = await ReadCategoryAsync(databaseContext, otherAccount.Id, "Loisirs");
        var repository = new CategoryRepository(databaseContext);

        Assert.Null(await repository.FindAsync(account.Id, otherLeisure.Id));
        Assert.NotNull(await repository.FindAsync(otherAccount.Id, otherLeisure.Id));
        Assert.Equal(8, (await repository.FindAllAsync(account.Id)).Count);
        Assert.All(await repository.FindAllAsync(account.Id), category => Assert.Equal(account.Id, category.UserId));
        Assert.Equal(8, await repository.CountAsync(account.Id));
        Assert.True((await repository.FindOtherCategoryAsync(account.Id))!.IsOtherCategory);
    }

    // La recherche d'un nom pris compare comme l'index unique : majuscules ignorées, accents comptés,
    // la catégorie renommée et les catégories des autres utilisateurs mises à part.
    [Fact]
    public async Task NameIsComparedWithoutCaseButWithAccents()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountWithCategoriesAsync(databaseContext, "test-categories-noms@exemple.test");
        var otherAccount = await SaveAccountWithCategoriesAsync(databaseContext, "test-categories-noms-autre@exemple.test");
        databaseContext.Categories.Add(new Category { Name = "Abonnements", UserId = otherAccount.Id });
        await databaseContext.SaveChangesAsync();
        var health = await ReadCategoryAsync(databaseContext, account.Id, "Santé");
        var repository = new CategoryRepository(databaseContext);

        Assert.True(await repository.IsNameUsedAsync(account.Id, "SANTÉ", null));
        Assert.True(await repository.IsNameUsedAsync(account.Id, "santé", null));
        Assert.False(await repository.IsNameUsedAsync(account.Id, "Sante", null));
        Assert.False(await repository.IsNameUsedAsync(account.Id, "SANTÉ", health.Id));
        Assert.False(await repository.IsNameUsedAsync(account.Id, "Abonnements", null));
    }

    // La recherche et l'index unique doivent toujours dire la même chose, même pour les lettres que .NET et PostgreSQL
    // ne mettent pas en minuscules de la même façon. Sinon, un nom accepté par la recherche serait refusé par la base à chaque essai.
    [Theory]
    [InlineData("Santé", "SANTÉ")]
    [InlineData("Straße", "STRASSE")]
    [InlineData("İstanbul", "istanbul")]
    [InlineData("ΣΟΦΙΑ", "σοφια")]
    [InlineData("Œuvres", "œuvres")]
    [InlineData("Ǆ", "ǆ")]
    public async Task NameSearchAgreesWithTheUniqueIndex(string savedName, string typedName)
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveConfirmedAccountAsync(databaseContext, "test-categories-index@exemple.test");
        databaseContext.Categories.Add(new Category { Name = savedName, UserId = account.Id });
        await databaseContext.SaveChangesAsync();
        var repository = new CategoryRepository(databaseContext);

        var isUsed = await repository.IsNameUsedAsync(account.Id, typedName, null);
        var isRefused = await IsRefusedByTheUniqueIndexAsync(databaseContext, account.Id, typedName);

        Assert.Equal(isRefused, isUsed);
    }

    [Fact]
    public async Task NewCategoryIsSavedWithTheOtherCategoryCheck()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountWithCategoriesAsync(databaseContext, "test-categories-creation@exemple.test");
        var repository = new CategoryRepository(databaseContext);
        var otherCategory = (await repository.FindOtherCategoryAsync(account.Id))!;

        repository.PrepareCategory(new Category { Name = "Abonnements", UserId = account.Id });
        repository.PrepareUnchangedCheck(otherCategory);

        Assert.True(await TrySaveChangesAsync(databaseContext));
        Assert.Equal(9, await databaseContext.Categories.CountAsync(row => row.UserId == account.Id));
        Assert.Equal("Autres", (await ReadCategoryAsync(databaseContext, account.Id, "Autres")).Name);
    }

    // Une autre création a réécrit « Autres » entre la lecture et l'enregistrement : la version de la ligne a changé.
    // L'enregistrement échoue sans rien garder, et le passage suivant recompte les catégories.
    [Fact]
    public async Task OtherCategoryWrittenMeanwhileMakesTheCreationAConflict()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountWithCategoriesAsync(databaseContext, "test-categories-version@exemple.test");
        var repository = new CategoryRepository(databaseContext);
        var otherCategory = (await repository.FindOtherCategoryAsync(account.Id))!;
        repository.PrepareCategory(new Category { Name = "Abonnements", UserId = account.Id });
        repository.PrepareUnchangedCheck(otherCategory);

        await WriteAsAnotherRequestAsync(databaseContext, async () =>
            await databaseContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE categorie SET nom_categorie = nom_categorie WHERE id_categorie = {otherCategory.Id}"));

        Assert.False(await TrySaveChangesAsync(databaseContext));
        Assert.False(await databaseContext.Categories.AnyAsync(row => row.UserId == account.Id && row.Name == "Abonnements"));
    }

    // Une autre requête a pris le même nom juste avant l'enregistrement : l'index unique refuse, et c'est un conflit, pas une panne.
    [Fact]
    public async Task SameNameSavedMeanwhileIsAConflict()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountWithCategoriesAsync(databaseContext, "test-categories-doublon@exemple.test");
        var repository = new CategoryRepository(databaseContext);
        repository.PrepareCategory(new Category { Name = "Abonnements", UserId = account.Id });

        await WriteAsAnotherRequestAsync(databaseContext, async () =>
            await databaseContext.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO categorie (nom_categorie, id_utilisateur) VALUES ('ABONNEMENTS', {account.Id})"));

        Assert.False(await TrySaveChangesAsync(databaseContext));
        Assert.Equal(1, await databaseContext.Categories.CountAsync(row =>
            row.UserId == account.Id && row.Name.ToLower() == "abonnements"));
    }

    // GR Dépenses : la catégorie disparaît, ses transactions vont dans « Autres », ses règles disparaissent,
    // et sa suggestion reste sans catégorie (MPD §1.3). Les autres catégories ne bougent pas.
    [Fact]
    public async Task DeletionMovesTransactionsToOtherAndRemovesRules()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountWithCategoriesAsync(databaseContext, "test-categories-suppression@exemple.test");
        var leisure = await ReadCategoryAsync(databaseContext, account.Id, "Loisirs");
        var health = await ReadCategoryAsync(databaseContext, account.Id, "Santé");
        var otherCategory = await ReadCategoryAsync(databaseContext, account.Id, "Autres");
        var cinema = await SaveTransactionAsync(databaseContext, account, leisure, "CINEMA");
        var pharmacy = await SaveTransactionAsync(databaseContext, account, health, "PHARMACIE");
        var suggestion = new SavingsSuggestion
        {
            Rule = SuggestionRule.CategoryOverrun,
            MonthlySavingsInCents = 1500,
            UserId = account.Id,
            CategoryId = leisure.Id
        };
        databaseContext.CategorizationRules.Add(new CategorizationRule { Label = "CINEMA", CategoryId = leisure.Id });
        databaseContext.SavingsSuggestions.Add(suggestion);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        var isDeleted = await new CategoryRepository(databaseContext).DeleteAsync(account.Id, leisure.Id);

        // La clé étrangère différée est vérifiée tout de suite : une transaction restée dans Loisirs la ferait échouer ici.
        await databaseContext.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
        Assert.True(isDeleted);
        Assert.False(await databaseContext.Categories.AnyAsync(row => row.Id == leisure.Id));
        Assert.Equal(otherCategory.Id, (await databaseContext.BankTransactions.AsNoTracking().SingleAsync(row => row.Id == cinema.Id)).CategoryId);
        Assert.Equal(health.Id, (await databaseContext.BankTransactions.AsNoTracking().SingleAsync(row => row.Id == pharmacy.Id)).CategoryId);
        Assert.False(await databaseContext.CategorizationRules.AnyAsync(row => row.CategoryId == leisure.Id));
        Assert.Null((await databaseContext.SavingsSuggestions.AsNoTracking().SingleAsync(row => row.Id == suggestion.Id)).CategoryId);
        Assert.Equal(7, await databaseContext.Categories.CountAsync(row => row.UserId == account.Id));
    }

    // « Autres », la catégorie d'un autre utilisateur et une catégorie inconnue ne sont jamais supprimées.
    [Fact]
    public async Task OtherCategoryAndCategoriesOfOthersAreNeverDeleted()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountWithCategoriesAsync(databaseContext, "test-categories-refus-a@exemple.test");
        var otherAccount = await SaveAccountWithCategoriesAsync(databaseContext, "test-categories-refus-b@exemple.test");
        var otherCategory = await ReadCategoryAsync(databaseContext, account.Id, "Autres");
        var leisure = await ReadCategoryAsync(databaseContext, account.Id, "Loisirs");
        var repository = new CategoryRepository(databaseContext);

        Assert.False(await repository.DeleteAsync(account.Id, otherCategory.Id));
        Assert.False(await repository.DeleteAsync(otherAccount.Id, leisure.Id));
        Assert.False(await repository.DeleteAsync(account.Id, Guid.NewGuid()));
        Assert.Equal(16, await databaseContext.Categories.CountAsync(row => row.UserId == account.Id || row.UserId == otherAccount.Id));
    }

    // La suppression est une écriture immédiate : elle n'emporte jamais des changements préparés pour l'unité de travail.
    [Fact]
    public async Task DeletionRefusesToCarryPendingChanges()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountWithCategoriesAsync(databaseContext, "test-categories-attente@exemple.test");
        var leisure = await ReadCategoryAsync(databaseContext, account.Id, "Loisirs");
        var repository = new CategoryRepository(databaseContext);
        repository.PrepareCategory(new Category { Name = "Abonnements", UserId = account.Id });

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.DeleteAsync(account.Id, leisure.Id));
        Assert.True(await databaseContext.Categories.AsNoTracking().AnyAsync(row => row.Id == leisure.Id));
    }

    // Chaîne réelle : les cinq cas d'utilisation avec le vrai dépôt, la vraie unité de travail et la vraie base.
    [Fact]
    public async Task CategoryLivesItsWholeLifeOnTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountWithCategoriesAsync(databaseContext, "test-categories-chaine@exemple.test");
        var repository = new CategoryRepository(databaseContext);
        var savingService = CreateSavingService(databaseContext);

        var created = await new CategoryCreationService(repository, savingService).CreateAsync(account.Id, " Abonnements ");
        var category = Assert.IsType<CategoryCreationResult.Created>(created).Category;
        var renamed = await new CategoryRenameService(repository, savingService).RenameAsync(account.Id, category.Id, "Streaming");
        var budget = await new CategoryBudgetService(repository, savingService).ReplaceAsync(account.Id, category.Id, 3000);
        var duplicate = await new CategoryCreationService(repository, savingService).CreateAsync(account.Id, "STREAMING");
        var transactionInCategory = await SaveTransactionAsync(databaseContext, account, category, "NETFLIX");
        var list = await new CategoryListService(repository).ListAsync(account.Id);
        var deleted = await new CategoryDeletionService(repository).DeleteAsync(account.Id, category.Id);

        await databaseContext.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
        Assert.IsType<CategoryRenameResult.Renamed>(renamed);
        Assert.IsType<CategoryBudgetResult.Changed>(budget);
        Assert.IsType<CategoryCreationResult.NameAlreadyUsed>(duplicate);
        Assert.Contains(list, row => row.Name == "Streaming" && row.MonthlyBudgetInCents == 3000);
        Assert.Equal("Autres", list.Last().Name);
        Assert.IsType<CategoryDeletionResult.Deleted>(deleted);
        var otherCategory = await ReadCategoryAsync(databaseContext, account.Id, "Autres");
        Assert.Equal(otherCategory.Id, (await databaseContext.BankTransactions.AsNoTracking().SingleAsync(row => row.Id == transactionInCategory.Id)).CategoryId);
    }

    // SS §6 : la 101e catégorie est refusée par la vraie chaîne.
    [Fact]
    public async Task HundredAndFirstCategoryIsRefusedOnTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountWithCategoriesAsync(databaseContext, "test-categories-limite@exemple.test");
        for (var number = 1; number <= 92; number++)
        {
            databaseContext.Categories.Add(new Category { Name = "Catégorie " + number, UserId = account.Id });
        }
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();
        var service = new CategoryCreationService(new CategoryRepository(databaseContext), CreateSavingService(databaseContext));

        var result = await service.CreateAsync(account.Id, "Une de trop");

        Assert.IsType<CategoryCreationResult.TooManyCategories>(result);
        Assert.Equal(100, await databaseContext.Categories.CountAsync(row => row.UserId == account.Id));
    }
}
