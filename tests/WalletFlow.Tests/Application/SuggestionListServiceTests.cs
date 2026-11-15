using WalletFlow.Application.Suggestions;
using WalletFlow.Domain.BagsAndSuggestions;
using static WalletFlow.Tests.Application.SuggestionServices;

namespace WalletFlow.Tests.Application;

// Vérifie le calcul et la liste des suggestions (GR Sacs, DD §8, §11 et §12), avec une fausse base.
// Le 5 novembre 2026, le mois évalué est octobre ; sa moyenne porte sur juillet, août et septembre.
public class SuggestionListServiceTests
{
    private readonly SuggestionServices services = new SuggestionServices();

    // Lit la liste de l'utilisateur des tests.
    private async Task<SuggestionList> ListAsync()
    {
        return await services.CreateList().ListAsync(services.UserId);
    }

    // Donne la seule suggestion de cette règle dans la liste.
    private static SuggestionView OnlyOf(SuggestionList list, SuggestionRule rule)
    {
        return list.Suggestions.Single(view => view.Rule == rule);
    }

    // Sans aucune opération, rien n'est évalué ni noté : le mois sera calculé quand la banque aura donné ses opérations.
    [Fact]
    public async Task NothingIsCalculatedBeforeTheFirstTransaction()
    {
        services.AddBankAccount(100_000);

        var list = await ListAsync();

        Assert.Equal(October, list.Month);
        Assert.Empty(list.Suggestions);
        Assert.Null(services.User.LastSuggestionMonth);
        Assert.Empty(services.SavedSuggestionsOfUser());
    }

    // Règle 1 : Alimentation dépense 100 € par mois en juillet, août et septembre, puis 130 € en octobre.
    // Transport dépense 120 € en octobre pour la même moyenne : 20 % pile ne suffit pas.
    [Fact]
    public async Task CategorySpendingMoreThanTwentyPercentAboveItsAverageIsProposed()
    {
        var account = services.AddBankAccount(0);
        services.StartHistoryBeforeJuly(account);
        services.SaveMonthlyExpenses(account, "Alimentation", 10_000, 10_000, 10_000, 13_000);
        services.SaveMonthlyExpenses(account, "Transport", 10_000, 10_000, 10_000, 12_000);

        var list = await ListAsync();

        var overrun = OnlyOf(list, SuggestionRule.CategoryOverrun);
        Assert.Equal(services.CategoryNamed("Alimentation").Id, overrun.CategoryId);
        Assert.Equal("Alimentation", overrun.CategoryName);
        Assert.Equal(October, overrun.Month);
        Assert.Equal(3_000, overrun.MonthlySavingsInCents);
        Assert.Equal(October, services.User.LastSuggestionMonth);
    }

    // Une catégorie de revenus, ou jamais utilisée avant octobre, ne déclenche rien, même avec une grosse dépense.
    [Fact]
    public async Task OnlySpendingCategoriesCanOverrun()
    {
        var account = services.AddBankAccount(0);
        services.SaveTransaction(account, "Alimentation", July, -1_000);
        foreach (var month in new[] { July, July.AddMonths(1), July.AddMonths(2) })
        {
            services.SaveTransaction(account, "Revenus", month.AddDays(2), 200_000);
        }
        services.SaveTransaction(account, "Revenus", October.AddDays(2), -5_000);
        services.SaveTransaction(account, "Loisirs", October.AddDays(5), -50_000);

        var list = await ListAsync();

        Assert.DoesNotContain(list.Suggestions, view => view.Rule == SuggestionRule.CategoryOverrun);
    }

    // Le dépassement demande 3 mois complets avant octobre : une première opération le 2 juillet ne suffit pas.
    [Fact]
    public async Task OverrunNeedsThreeFullMonthsOfHistory()
    {
        var account = services.AddBankAccount(0);
        services.SaveTransaction(account, "Alimentation", July.AddDays(1), -10_000);
        services.SaveTransaction(account, "Alimentation", July.AddMonths(1), -10_000);
        services.SaveTransaction(account, "Alimentation", July.AddMonths(2), -10_000);
        services.SaveTransaction(account, "Alimentation", October, -50_000);

        var list = await ListAsync();

        Assert.DoesNotContain(list.Suggestions, view => view.Rule == SuggestionRule.CategoryOverrun);
        Assert.Equal(October, services.User.LastSuggestionMonth);
    }

    // Règle 2 : 1 000 € sur le compte, dont 300 € dans un sac : 700 € restent disponibles.
    [Fact]
    public async Task MoneyOutsideTheBagsIsProposed()
    {
        var account = services.AddBankAccount(100_000);
        services.SaveTransaction(account, "Alimentation", October, -1_000);
        var bag = services.SaveBag("Vacances");
        services.Bags.SavedMovements.Add(new BagMovement { AmountInCents = 30_000, MovedAt = services.Now, DestinationBagId = bag.Id });

        var list = await ListAsync();

        Assert.Equal(70_000, OnlyOf(list, SuggestionRule.UnallocatedMoney).MonthlySavingsInCents);
    }

    // Sans argent disponible, ou quand les sacs dépassent le solde, la règle 2 ne propose rien.
    [Theory]
    [InlineData(30_000)]
    [InlineData(45_000)]
    public async Task NoAvailableMoneyMeansNoUnallocatedSuggestion(int bagBalanceInCents)
    {
        var account = services.AddBankAccount(30_000);
        services.SaveTransaction(account, "Alimentation", October, -1_000);
        var bag = services.SaveBag("Vacances");
        services.Bags.SavedMovements.Add(new BagMovement { AmountInCents = bagBalanceInCents, MovedAt = services.Now, DestinationBagId = bag.Id });

        var list = await ListAsync();

        Assert.DoesNotContain(list.Suggestions, view => view.Rule == SuggestionRule.UnallocatedMoney);
    }

    // Règle 3 : 2 000 € de revenus et 1 700 € de dépenses en octobre : l'épargne de 300 € manque 100 € pour atteindre 20 %.
    // Les catégories comptent pour leur total net, comme dans les graphiques : un remboursement réduit sa dépense.
    [Fact]
    public async Task SavingsBelowTwentyPercentOfIncomeAreProposed()
    {
        var account = services.AddBankAccount(0);
        services.StartHistoryBeforeJuly(account);
        services.SaveTransaction(account, "Revenus", October.AddDays(1), 200_000);
        services.SaveTransaction(account, "Logement", October.AddDays(2), -150_000);
        services.SaveTransaction(account, "Santé", October.AddDays(3), -25_000);
        services.SaveTransaction(account, "Santé", October.AddDays(8), 5_000);

        var list = await ListAsync();

        Assert.Equal(10_000, OnlyOf(list, SuggestionRule.InsufficientSavings).MonthlySavingsInCents);
    }

    // 20 % pile d'épargne, un mois sans revenus, ou un mois dont le début n'est pas connu : la règle 3 ne propose rien.
    [Theory]
    [InlineData(200_000, -160_000, 0)]
    [InlineData(0, -160_000, 0)]
    [InlineData(200_000, -190_000, 1)]
    public async Task SavingsRuleNeedsIncomeAndAFullMonth(int incomeInCents, int expenseInCents, int firstDayOffset)
    {
        var account = services.AddBankAccount(0);
        if (incomeInCents > 0)
        {
            services.SaveTransaction(account, "Revenus", October.AddDays(firstDayOffset), incomeInCents);
        }
        services.SaveTransaction(account, "Logement", October.AddDays(firstDayOffset + 1), expenseInCents);

        var list = await ListAsync();

        Assert.DoesNotContain(list.Suggestions, view => view.Rule == SuggestionRule.InsufficientSavings);
    }

    // Un transfert entre deux comptes ne compte ni comme revenu ni comme dépense (GR Comptes).
    [Fact]
    public async Task TransfersNeverCountInTheRules()
    {
        var account = services.AddBankAccount(0);
        services.StartHistoryBeforeJuly(account);
        services.SaveTransaction(account, "Revenus", October.AddDays(1), 200_000);
        services.SaveTransaction(account, "Logement", October.AddDays(2), -160_000);
        services.Bank.SaveTransaction(account, "Autres", October.AddDays(3), -50_000, isInternalTransfer: true);

        var list = await ListAsync();

        Assert.DoesNotContain(list.Suggestions, view => view.Rule == SuggestionRule.InsufficientSavings);
    }

    // Les suggestions d'un mois ne sont calculées qu'une fois : de l'argent qui arrive ensuite ne crée rien de plus,
    // et la suggestion déjà calculée garde son montant.
    [Fact]
    public async Task MonthIsCalculatedOnlyOnce()
    {
        var account = services.AddBankAccount(10_000);
        services.SaveTransaction(account, "Alimentation", October, -1_000);
        await ListAsync();

        services.AddBankAccount(90_000);
        services.SaveMonthlyExpenses(account, "Transport", 10_000, 10_000, 10_000, 50_000);
        var list = await ListAsync();

        Assert.Equal(10_000, OnlyOf(list, SuggestionRule.UnallocatedMoney).MonthlySavingsInCents);
        Assert.Single(list.Suggestions);
        Assert.Single(services.SavedSuggestionsOfUser());
    }

    // Au calcul d'octobre : les suggestions de septembre sans réponse sont effacées, comme une suggestion ignorée il y a plus
    // de 30 jours. Une suggestion acceptée reste, comme une suggestion ignorée il y a moins de 30 jours.
    [Fact]
    public async Task NewMonthClearsWhatIsNoLongerNeeded()
    {
        var account = services.AddBankAccount(0);
        services.SaveTransaction(account, "Alimentation", October, -1_000);
        var oldProposal = services.SaveSuggestion(SuggestionRule.UnallocatedMoney, September, 4_000);
        var accepted = services.SaveSuggestion(SuggestionRule.InsufficientSavings, September, 2_000, SuggestionStatus.Accepted, statusChangedAt: services.Now.AddDays(-40));
        var oldIgnored = services.SaveSuggestion(SuggestionRule.UnallocatedMoney, September.AddMonths(-1), 3_000, SuggestionStatus.Ignored, statusChangedAt: services.Now.AddDays(-30));
        var recentIgnored = services.SaveSuggestion(SuggestionRule.UnallocatedMoney, September, 3_000, SuggestionStatus.Ignored, statusChangedAt: services.Now.AddDays(-29));

        await ListAsync();

        var savedIds = services.SavedSuggestionsOfUser().Select(suggestion => suggestion.Id).ToList();
        Assert.DoesNotContain(oldProposal.Id, savedIds);
        Assert.DoesNotContain(oldIgnored.Id, savedIds);
        Assert.Contains(accepted.Id, savedIds);
        Assert.Contains(recentIgnored.Id, savedIds);
    }

    // Une suggestion ignorée il y a moins de 30 jours met en pause sa règle, et pour un dépassement sa seule catégorie :
    // Transport et les deux autres règles restent proposés.
    [Fact]
    public async Task IgnoredSuggestionPausesOnlyItsRuleAndCategory()
    {
        var account = services.AddBankAccount(100_000);
        services.StartHistoryBeforeJuly(account);
        services.SaveMonthlyExpenses(account, "Alimentation", 10_000, 10_000, 10_000, 20_000);
        services.SaveMonthlyExpenses(account, "Transport", 10_000, 10_000, 10_000, 20_000);
        services.SaveTransaction(account, "Revenus", October.AddDays(1), 45_000);
        services.SaveSuggestion(SuggestionRule.CategoryOverrun, September, 5_000, SuggestionStatus.Ignored, services.CategoryNamed("Alimentation").Id, services.Now.AddDays(-10));

        var list = await ListAsync();

        Assert.Equal(services.CategoryNamed("Transport").Id, OnlyOf(list, SuggestionRule.CategoryOverrun).CategoryId);
        Assert.Single(list.Suggestions, view => view.Rule == SuggestionRule.UnallocatedMoney);
        Assert.Single(list.Suggestions, view => view.Rule == SuggestionRule.InsufficientSavings);
    }

    // Une règle sans catégorie ignorée il y a moins de 30 jours ne propose rien de nouveau.
    [Fact]
    public async Task IgnoredRuleWithoutCategoryIsPaused()
    {
        var account = services.AddBankAccount(100_000);
        services.SaveTransaction(account, "Alimentation", October, -1_000);
        services.SaveSuggestion(SuggestionRule.UnallocatedMoney, September, 5_000, SuggestionStatus.Ignored, statusChangedAt: services.Now.AddDays(-29));

        var list = await ListAsync();

        Assert.Empty(list.Suggestions);
    }

    // La liste ne montre que les suggestions proposées d'octobre de l'utilisateur, les plus grosses économies d'abord.
    // Une suggestion d'un autre, déjà répondue, ou retirée avec sa catégorie n'y est jamais (SS §4).
    [Fact]
    public async Task ListShowsOnlyTheUsersProposalsOfTheMonth()
    {
        var account = services.AddBankAccount(0);
        services.SaveTransaction(account, "Alimentation", October, -1_000);
        services.User.NoteSuggestionMonth(October);
        var small = services.SaveSuggestion(SuggestionRule.UnallocatedMoney, October, 1_000);
        var big = services.SaveSuggestion(SuggestionRule.CategoryOverrun, October, 9_000, categoryId: services.CategoryNamed("Transport").Id);
        services.SaveSuggestion(SuggestionRule.InsufficientSavings, October, 5_000, SuggestionStatus.Accepted, statusChangedAt: services.Now);
        services.SaveSuggestion(SuggestionRule.CategoryOverrun, October, 8_000);
        services.SaveSuggestion(SuggestionRule.InsufficientSavings, October, 7_000, userId: Guid.NewGuid());

        var list = await ListAsync();

        Assert.Equal(new List<Guid> { big.Id, small.Id }, list.Suggestions.Select(view => view.Id).ToList());
        Assert.Equal("Transport", list.Suggestions[0].CategoryName);
        Assert.Null(list.Suggestions[1].CategoryName);
    }

    // DD §11 : les économies réalisées additionnent les mouvements des suggestions acceptées de l'utilisateur,
    // pas ceux d'un autre utilisateur, ni un déplacement ordinaire.
    [Fact]
    public async Task SavedMoneyCountsOnlyTheUsersAcceptedSuggestions()
    {
        var bag = services.SaveBag("Épargne");
        var accepted = services.SaveSuggestion(SuggestionRule.UnallocatedMoney, September, 4_000, SuggestionStatus.Accepted, statusChangedAt: services.Now);
        var ofOther = services.SaveSuggestion(SuggestionRule.UnallocatedMoney, September, 4_000, SuggestionStatus.Accepted, statusChangedAt: services.Now, userId: Guid.NewGuid());
        services.Bags.SavedMovements.Add(new BagMovement { AmountInCents = 2_500, MovedAt = services.Now, DestinationBagId = bag.Id, SavingsSuggestionId = accepted.Id });
        services.Bags.SavedMovements.Add(new BagMovement { AmountInCents = 9_000, MovedAt = services.Now, DestinationBagId = bag.Id, SavingsSuggestionId = ofOther.Id });
        services.Bags.SavedMovements.Add(new BagMovement { AmountInCents = 7_000, MovedAt = services.Now, DestinationBagId = bag.Id });

        var list = await ListAsync();

        Assert.Equal(2_500, list.SavedInCents);
    }

    // Une autre lecture calcule octobre juste avant l'enregistrement : le passage suivant trouve le mois déjà noté,
    // et ne crée rien en double.
    [Fact]
    public async Task CalculationSavedMeanwhileIsNotRepeated()
    {
        var account = services.AddBankAccount(50_000);
        services.SaveTransaction(account, "Alimentation", October, -1_000);
        SavingsSuggestion? savedByOther = null;
        services.Suggestions.OtherRequestBeforeSave = () =>
        {
            services.User.NoteSuggestionMonth(October);
            savedByOther = services.SaveSuggestion(SuggestionRule.UnallocatedMoney, October, 50_000);
        };

        var list = await ListAsync();

        Assert.Equal(savedByOther!.Id, Assert.Single(list.Suggestions).Id);
        Assert.Single(services.SavedSuggestionsOfUser());
    }

    // Du 1er au 4 novembre, octobre n'est pas encore calculé : la liste montre toujours septembre, déjà calculé.
    // Le 5, octobre est calculé, et la suggestion de septembre restée sans réponse est effacée.
    [Fact]
    public async Task OctoberIsCalculatedOnTheFifth()
    {
        var early = new SuggestionServices(0);
        var account = early.AddBankAccount(50_000);
        early.SaveTransaction(account, "Alimentation", October, -1_000);
        early.User.NoteSuggestionMonth(September);
        var september = early.SaveSuggestion(SuggestionRule.UnallocatedMoney, September, 4_000);

        var onTheFourth = await early.CreateList().ListAsync(early.UserId);
        early.Bank.Clock.Advance(TimeSpan.FromDays(1));
        var onTheFifth = await early.CreateList().ListAsync(early.UserId);

        Assert.Equal(September, onTheFourth.Month);
        Assert.Equal(september.Id, Assert.Single(onTheFourth.Suggestions).Id);
        Assert.Equal(October, onTheFifth.Month);
        Assert.Equal(SuggestionRule.UnallocatedMoney, Assert.Single(onTheFifth.Suggestions).Rule);
        Assert.Equal(October, Assert.Single(early.SavedSuggestionsOfUser()).Month);
        Assert.Equal(October, early.User.LastSuggestionMonth);
    }

    // Sans opération, le calcul n'a pas lieu : une ancienne suggestion sans réponse, restée d'un mois d'avant, n'est pas montrée.
    // La liste ne montre que le mois évalué.
    [Fact]
    public async Task ListNeverShowsAnotherMonth()
    {
        services.AddBankAccount(100_000);
        services.SaveSuggestion(SuggestionRule.UnallocatedMoney, September, 4_000);

        var list = await ListAsync();

        Assert.Empty(list.Suggestions);
        Assert.Single(services.SavedSuggestionsOfUser());
    }

    // Une suggestion d'octobre déjà enregistrée alors que le mois n'est pas noté n'est jamais créée une seconde fois :
    // l'index unique la refuserait à chaque essai, et le mois ne serait jamais noté.
    [Fact]
    public async Task SuggestionAlreadySavedIsNotCreatedAgain()
    {
        var account = services.AddBankAccount(50_000);
        services.SaveTransaction(account, "Alimentation", October, -1_000);
        var existing = services.SaveSuggestion(SuggestionRule.UnallocatedMoney, October, 50_000);

        var list = await ListAsync();

        Assert.Equal(existing.Id, Assert.Single(list.Suggestions).Id);
        Assert.Single(services.SavedSuggestionsOfUser());
        Assert.Equal(October, services.User.LastSuggestionMonth);
    }

    // Un déplacement d'argent enregistré par une autre requête juste avant l'enregistrement a réécrit la ligne de l'utilisateur :
    // le calcul est refait, avec l'argent disponible à jour.
    [Fact]
    public async Task MovementSavedMeanwhileMakesTheCalculationRedo()
    {
        var account = services.AddBankAccount(50_000);
        services.SaveTransaction(account, "Alimentation", October, -1_000);
        services.Bags.MovementSavedBeforeSave = new BagMovement
        {
            AmountInCents = 20_000,
            MovedAt = services.Now,
            DestinationBagId = services.SaveBag("Vacances").Id
        };

        var list = await ListAsync();

        Assert.Equal(30_000, Assert.Single(list.Suggestions).MonthlySavingsInCents);
        Assert.Single(services.SavedSuggestionsOfUser());
    }

    // Des conflits passagers ne perdent pas le calcul : il est refait et enregistré une seule fois. S'ils ne cessent pas,
    // rien n'est noté, et la lecture suivante calculera le mois.
    [Fact]
    public async Task ConflictsDelayTheCalculationWithoutLosingIt()
    {
        var account = services.AddBankAccount(50_000);
        services.SaveTransaction(account, "Alimentation", October, -1_000);
        services.Suggestions.Conflicts = 100;

        var blocked = await ListAsync();
        services.Suggestions.Conflicts = 3;
        var calculated = await ListAsync();

        Assert.Empty(blocked.Suggestions);
        Assert.Single(calculated.Suggestions);
        Assert.Single(services.SavedSuggestionsOfUser());
        Assert.Equal(October, services.User.LastSuggestionMonth);
    }
}
