using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Bags;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Suggestions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Infrastructure.BankTestRows;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les suggestions dans la vraie base PostgreSQL locale, avec l'utilisateur de l'API (GR Sacs, SS §4 et §5, DD §8, MPD).
// Chaque test travaille dans une transaction annulée à la fin : la base reste vide.
public class SuggestionRepositoryTests
{
    private static readonly DateOnly October = new DateOnly(2026, 10, 1);

    // Enregistre ces suggestions, puis oublie les objets gardés en mémoire.
    private static async Task SaveSuggestionsAsync(WalletFlowDatabaseContext databaseContext, params SavingsSuggestion[] suggestions)
    {
        databaseContext.SavingsSuggestions.AddRange(suggestions);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();
    }

    // Crée une suggestion proposée de cet utilisateur pour octobre, sans l'enregistrer.
    private static SavingsSuggestion CreateSuggestion(
        Guid userId,
        SuggestionRule rule = SuggestionRule.UnallocatedMoney,
        Guid? categoryId = null,
        SuggestionStatus status = SuggestionStatus.Proposed)
    {
        return new SavingsSuggestion
        {
            Rule = rule,
            Month = October,
            MonthlySavingsInCents = 5_000,
            Status = status,
            UserId = userId,
            CategoryId = categoryId
        };
    }

    // Lit la catégorie de ce compte qui porte ce nom.
    private static async Task<Category> ReadCategoryAsync(WalletFlowDatabaseContext databaseContext, Guid userId, string name)
    {
        return await databaseContext.Categories.AsNoTracking().SingleAsync(category => category.UserId == userId && category.Name == name);
    }

    // SS §4 : chaque recherche ne voit que les suggestions de l'utilisateur, même avec l'identifiant de celle d'un autre.
    [Fact]
    public async Task SearchesSeeOnlyTheSuggestionsOfTheUser()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveConfirmedAccountAsync(databaseContext, "test-suggestions-acces@exemple.test");
        var otherUser = await SaveConfirmedAccountAsync(databaseContext, "test-suggestions-acces-autre@exemple.test");
        var own = CreateSuggestion(user.Id);
        var ofOther = CreateSuggestion(otherUser.Id);
        await SaveSuggestionsAsync(databaseContext, own, ofOther);
        var repository = new SuggestionRepository(databaseContext);

        Assert.Null(await repository.FindAsync(user.Id, ofOther.Id));
        Assert.Equal(own.Id, (await repository.FindAsync(user.Id, own.Id))?.Id);
        Assert.Equal(new List<Guid> { own.Id }, (await repository.FindAllAsync(user.Id)).Select(suggestion => suggestion.Id).ToList());
    }

    // DD §11 : les économies réalisées additionnent les mouvements des suggestions acceptées de l'utilisateur. Un mouvement
    // ordinaire ou celui d'un autre n'y est pas. Supprimer le sac efface son mouvement depuis l'argent disponible : l'argent
    // n'est plus placé, et il n'est plus compté.
    [Fact]
    public async Task SavedMoneyCountsOnlyTheUsersAcceptedMovements()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveConfirmedAccountAsync(databaseContext, "test-suggestions-economies@exemple.test");
        var otherUser = await SaveConfirmedAccountAsync(databaseContext, "test-suggestions-economies-autre@exemple.test");
        var savingsBag = new Bag { Name = "Épargne", UserId = user.Id };
        var holidaysBag = new Bag { Name = "Vacances", UserId = user.Id };
        var otherBag = new Bag { Name = "Épargne", UserId = otherUser.Id };
        var accepted = CreateSuggestion(user.Id, status: SuggestionStatus.Accepted);
        var secondAccepted = CreateSuggestion(user.Id, SuggestionRule.InsufficientSavings, status: SuggestionStatus.Accepted);
        var acceptedOfOther = CreateSuggestion(otherUser.Id, status: SuggestionStatus.Accepted);
        databaseContext.Bags.AddRange(savingsBag, holidaysBag, otherBag);
        await SaveSuggestionsAsync(databaseContext, accepted, secondAccepted, acceptedOfOther);
        databaseContext.BagMovements.AddRange(
            new BagMovement { AmountInCents = 2_500, MovedAt = Now, DestinationBagId = savingsBag.Id, SavingsSuggestionId = accepted.Id },
            new BagMovement { AmountInCents = 1_000, MovedAt = Now, DestinationBagId = holidaysBag.Id, SavingsSuggestionId = secondAccepted.Id },
            new BagMovement { AmountInCents = 9_000, MovedAt = Now, DestinationBagId = otherBag.Id, SavingsSuggestionId = acceptedOfOther.Id },
            new BagMovement { AmountInCents = 7_000, MovedAt = Now, DestinationBagId = savingsBag.Id });
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();
        var repository = new SuggestionRepository(databaseContext);

        Assert.Equal(3_500, await repository.SumSavedAsync(user.Id));

        await databaseContext.Bags.Where(bag => bag.Id == holidaysBag.Id).ExecuteDeleteAsync();

        Assert.Equal(2_500, await repository.SumSavedAsync(user.Id));
        Assert.Equal(0, await repository.SumSavedAsync(Guid.NewGuid()));
    }

    // L'application ne peut pas écrire le repère à l'inscription (droits de walletflow_app) : EF Core ne l'envoie pas,
    // l'inscription passe, et le repère reste vide. Ensuite, l'application peut le noter.
    [Fact]
    public async Task SuggestionMonthIsNeverInsertedButCanBeNoted()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = new User
        {
            Email = "test-suggestions-repere@exemple.test",
            PasswordHash = "empreinte-de-test",
            LastSuggestionMonth = October
        };
        databaseContext.Users.Add(user);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        var saved = await databaseContext.Users.SingleAsync(row => row.Id == user.Id);
        Assert.Null(saved.LastSuggestionMonth);

        saved.NoteSuggestionMonth(October);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        Assert.Equal(October, (await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Id == user.Id)).LastSuggestionMonth);
    }

    // Une autre lecture a enregistré la même suggestion du même mois juste avant : l'index unique refuse, et c'est un conflit,
    // pas une panne. Rien n'est gardé en double, pour une règle sans catégorie comme pour une catégorie dépassée.
    [Theory]
    [InlineData(SuggestionRule.UnallocatedMoney)]
    [InlineData(SuggestionRule.CategoryOverrun)]
    public async Task SameSuggestionSavedMeanwhileIsAConflict(SuggestionRule rule)
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveAccountAsync(databaseContext, "test-suggestions-doublon@exemple.test");
        Guid? categoryId = rule == SuggestionRule.CategoryOverrun ? (await ReadCategoryAsync(databaseContext, user.Id, "Loisirs")).Id : null;
        var ruleCode = rule == SuggestionRule.CategoryOverrun ? "depassement_categorie" : "argent_disponible";
        new SuggestionRepository(databaseContext).PrepareSuggestion(CreateSuggestion(user.Id, rule, categoryId));

        await WriteAsAnotherRequestAsync(databaseContext, async () =>
            await databaseContext.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO suggestion (regle_suggestion, mois_suggestion, economie_mensuelle, id_utilisateur, id_categorie) VALUES ({ruleCode}, {October}, 4000, {user.Id}, {categoryId})"));

        Assert.False(await TrySaveChangesAsync(databaseContext));
        Assert.Equal(1, await databaseContext.SavingsSuggestions.CountAsync(row => row.UserId == user.Id));
    }

    // La catégorie dépassée a été supprimée juste avant l'enregistrement : la clé étrangère refuse, et c'est un conflit.
    // Le passage suivant relit les opérations, qui sont passées dans « Autres ».
    [Fact]
    public async Task CategoryDeletedMeanwhileIsAConflict()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveAccountAsync(databaseContext, "test-suggestions-categorie@exemple.test");
        var leisure = await ReadCategoryAsync(databaseContext, user.Id, "Loisirs");
        new SuggestionRepository(databaseContext).PrepareSuggestion(CreateSuggestion(user.Id, SuggestionRule.CategoryOverrun, leisure.Id));

        await WriteAsAnotherRequestAsync(databaseContext, async () =>
            await databaseContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM categorie WHERE id_categorie = {leisure.Id}"));

        Assert.False(await TrySaveChangesAsync(databaseContext));
        Assert.False(await databaseContext.SavingsSuggestions.AnyAsync(row => row.UserId == user.Id));
    }

    // Chaîne réelle, le 5 novembre 2026 : calcul, acceptation et ignorance avec les vrais dépôts, la vraie unité de travail
    // et la vraie base.
    // 1 000 € sur le compte, Alimentation à 100 € par mois puis 150 € en octobre, et 2 000 € de revenus pour 1 750 € de dépenses.
    [Fact]
    public async Task SuggestionsLiveTheirWholeLifeOnTheRealDatabase()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveAccountAsync(databaseContext, "test-suggestions-chaine@exemple.test");
        var (_, account) = await SaveConnectionAsync(databaseContext, user.Id, "compte-suggestions");
        await databaseContext.BankAccounts.Where(row => row.Id == account.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.BalanceInCents, 100_000));
        var food = await ReadCategoryAsync(databaseContext, user.Id, "Alimentation");
        var housing = await ReadCategoryAsync(databaseContext, user.Id, "Logement");
        var income = await ReadCategoryAsync(databaseContext, user.Id, "Revenus");
        await SaveTransactionsAsync(
            databaseContext,
            CreateTransaction(account.Id, food.Id, "juillet", amountInCents: -10_000, date: new DateOnly(2026, 7, 1)),
            CreateTransaction(account.Id, food.Id, "aout", amountInCents: -10_000, date: new DateOnly(2026, 8, 3)),
            CreateTransaction(account.Id, food.Id, "septembre", amountInCents: -10_000, date: new DateOnly(2026, 9, 3)),
            CreateTransaction(account.Id, food.Id, "octobre", amountInCents: -15_000, date: new DateOnly(2026, 10, 3)),
            CreateTransaction(account.Id, housing.Id, "loyer", amountInCents: -160_000, date: new DateOnly(2026, 10, 5)),
            CreateTransaction(account.Id, income.Id, "salaire", amountInCents: 200_000, date: new DateOnly(2026, 10, 1)));
        var bag = new Bag { Name = "Épargne", UserId = user.Id };
        databaseContext.Bags.Add(bag);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();
        var clock = new FakeTimeProvider();
        clock.Advance(TimeSpan.FromDays(1));
        var bagRepository = new BagRepository(databaseContext);
        var suggestionRepository = new SuggestionRepository(databaseContext);
        var balanceTotal = new BankBalanceTotalService(new BankConnectionRepository(databaseContext));
        var calculation = new SuggestionCalculationService(
            bagRepository, suggestionRepository, new TransactionRepository(databaseContext),
            CreateAlertPreparation(databaseContext), balanceTotal, CreateSavingService(databaseContext), clock);
        var list = new SuggestionListService(calculation, suggestionRepository, new CategoryRepository(databaseContext));
        var acceptance = new SuggestionAcceptanceService(
            bagRepository, suggestionRepository, new BagMovementChecks(bagRepository, balanceTotal), CreateBagGoalAlerts(databaseContext),
            CreateSavingService(databaseContext), clock);
        var dismissal = new SuggestionDismissalService(bagRepository, suggestionRepository, CreateSavingService(databaseContext), clock);

        var beforeCalculation = await list.ListAsync(user.Id);
        await calculation.CalculateAsync(user.Id, October);
        var first = await list.ListAsync(user.Id);
        var second = await list.ListAsync(user.Id);

        Assert.Empty(beforeCalculation.Suggestions);

        Assert.Equal(3, first.Suggestions.Count);
        Assert.Equal(first.Suggestions, second.Suggestions);
        Assert.Equal(3, await databaseContext.SavingsSuggestions.CountAsync(row => row.UserId == user.Id));
        Assert.Equal(October, (await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Id == user.Id)).LastSuggestionMonth);
        Assert.Equal(new List<int> { 100_000, 15_000, 5_000 }, first.Suggestions.Select(view => view.MonthlySavingsInCents).ToList());
        Assert.Equal("Alimentation", first.Suggestions.Single(view => view.Rule == SuggestionRule.CategoryOverrun).CategoryName);
        var monthAlert = await databaseContext.Alerts.AsNoTracking().SingleAsync(row => row.UserId == user.Id);
        Assert.Equal((AlertKind.NewSuggestion, (DateOnly?)October), (monthAlert.Kind, monthAlert.Month));
        Assert.Equal(monthAlert.Id, (await databaseContext.EmailRequests.AsNoTracking().SingleAsync(row => row.UserId == user.Id)).AlertId);

        var unallocated = first.Suggestions.Single(view => view.Rule == SuggestionRule.UnallocatedMoney);
        var overrun = first.Suggestions.Single(view => view.Rule == SuggestionRule.CategoryOverrun);
        Assert.IsType<SuggestionAcceptanceResult.Accepted>(await acceptance.AcceptAsync(user.Id, unallocated.Id, bag.Id, 40_000));
        Assert.IsType<SuggestionDismissalResult.Dismissed>(await dismissal.DismissAsync(user.Id, overrun.Id));
        Assert.IsType<SuggestionAcceptanceResult.AlreadyAnswered>(await acceptance.AcceptAsync(user.Id, unallocated.Id, bag.Id, 1));

        var after = await list.ListAsync(user.Id);
        Assert.Equal(SuggestionRule.InsufficientSavings, Assert.Single(after.Suggestions).Rule);
        Assert.Equal(40_000, after.SavedInCents);
        var answered = await databaseContext.SavingsSuggestions.AsNoTracking().Where(row => row.UserId == user.Id).ToListAsync();
        Assert.Equal(SuggestionStatus.Accepted, answered.Single(row => row.Id == unallocated.Id).Status);
        Assert.Equal(SuggestionStatus.Ignored, answered.Single(row => row.Id == overrun.Id).Status);
        Assert.Equal(unallocated.Id, (await databaseContext.BagMovements.AsNoTracking().SingleAsync(row => row.DestinationBagId == bag.Id)).SavingsSuggestionId);
    }

    // La tâche de fond trouve les comptes confirmés qui ont au moins une opération et dont le mois attend son calcul,
    // dans l'ordre de leur uuid, à partir de celui qui suit le dernier lu. La base compare les uuid elle-même.
    [Fact]
    public async Task UsersToCalculateAreFoundInUuidOrderAfterTheLastOneRead()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var first = await SaveUserWithTransactionAsync(databaseContext, "test-suggestions-tache-1@exemple.test", true);
        var second = await SaveUserWithTransactionAsync(databaseContext, "test-suggestions-tache-2@exemple.test", true);
        var unconfirmed = await SaveUserWithTransactionAsync(databaseContext, "test-suggestions-tache-3@exemple.test", false);
        var calculated = await SaveUserWithTransactionAsync(databaseContext, "test-suggestions-tache-4@exemple.test", true);
        await databaseContext.Users.Where(row => row.Id == calculated.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LastSuggestionMonth, October));
        var withoutTransaction = await SaveAccountAsync(databaseContext, "test-suggestions-tache-5@exemple.test");
        await SaveConnectionAsync(databaseContext, withoutTransaction.Id, "compte-sans-operation-" + Guid.NewGuid().ToString("N"));
        var repository = new SuggestionRepository(databaseContext);
        var ordered = new List<Guid> { first.Id, second.Id }.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToList();

        var dueUsers = await repository.FindUsersToCalculateAsync(October, null, 100_000);
        var afterFirst = await repository.FindUsersToCalculateAsync(October, ordered[0], 100_000);
        var afterSecond = await repository.FindUsersToCalculateAsync(October, ordered[1], 100_000);
        var nextMonth = await repository.FindUsersToCalculateAsync(October.AddMonths(1), null, 100_000);

        Assert.Equal(ordered, dueUsers.Where(ordered.Contains).ToList());
        Assert.DoesNotContain(unconfirmed.Id, dueUsers);
        Assert.DoesNotContain(calculated.Id, dueUsers);
        Assert.DoesNotContain(withoutTransaction.Id, dueUsers);
        Assert.DoesNotContain(ordered[0], afterFirst);
        Assert.Contains(ordered[1], afterFirst);
        Assert.DoesNotContain(ordered[1], afterSecond);
        Assert.Contains(calculated.Id, nextMonth);
        Assert.Equal(dueUsers.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToList(), dueUsers);
    }

    // Enregistre un compte, confirmé ou non, avec une connexion et une opération d'octobre.
    private static async Task<User> SaveUserWithTransactionAsync(WalletFlowDatabaseContext databaseContext, string email, bool isEmailConfirmed)
    {
        var user = await SaveAccountAsync(databaseContext, email);

        if (!isEmailConfirmed)
        {
            await databaseContext.Users.Where(row => row.Id == user.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsEmailConfirmed, false));
        }

        var (_, account) = await SaveConnectionAsync(databaseContext, user.Id, "compte-" + Guid.NewGuid().ToString("N"));
        var food = await ReadCategoryAsync(databaseContext, user.Id, "Alimentation");
        await SaveTransactionsAsync(databaseContext, CreateTransaction(account.Id, food.Id, "octobre-" + Guid.NewGuid().ToString("N"), date: October.AddDays(2)));

        return user;
    }
}
