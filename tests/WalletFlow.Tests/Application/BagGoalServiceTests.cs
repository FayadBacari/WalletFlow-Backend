using WalletFlow.Application.Bags;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie l'objectif d'un sac (GR Sacs, SS §4, DD §7.1 et §11).
public class BagGoalServiceTests
{
    [Fact]
    public async Task GoalIsSetAndTheProgressComesFromTheBalance()
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");
        services.SaveMovement(null, holidays, 50_000);
        var targetDate = services.Today.AddDays(200);

        var result = await services.CreateGoal().ReplaceAsync(services.UserId, holidays.Id, 100_000, targetDate);

        var changed = Assert.IsType<BagGoalResult.Changed>(result);
        Assert.Equal(100_000, holidays.TargetAmountInCents);
        Assert.Equal(targetDate, holidays.TargetDate);
        Assert.Equal(50, changed.Bag.ProgressPercent);
        Assert.Equal(50_000, changed.Bag.BalanceInCents);
    }

    [Fact]
    public async Task GoalIsRemovedWithTwoNulls()
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");
        holidays.ReplaceGoal(100_000, services.Today.AddDays(10), services.Today);

        var result = await services.CreateGoal().ReplaceAsync(services.UserId, holidays.Id, null, null);

        var changed = Assert.IsType<BagGoalResult.Changed>(result);
        Assert.Null(holidays.TargetAmountInCents);
        Assert.Null(holidays.TargetDate);
        Assert.Null(changed.Bag.ProgressPercent);
    }

    // Un objectif refusé ne change rien, et la raison est donnée.
    [Theory]
    [InlineData(0, null, BagGoalProblem.AmountBreaksRules)]
    [InlineData(null, 30, BagGoalProblem.DateWithoutAmount)]
    [InlineData(500, 0, BagGoalProblem.DateTooEarly)]
    public async Task GoalThatBreaksTheRulesChangesNothing(int? targetAmountInCents, int? daysFromToday, BagGoalProblem expectedProblem)
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");
        holidays.ReplaceGoal(100_000, null, services.Today);
        DateOnly? targetDate = daysFromToday == null ? null : services.Today.AddDays(daysFromToday.Value);

        var result = await services.CreateGoal().ReplaceAsync(services.UserId, holidays.Id, targetAmountInCents, targetDate);

        Assert.Equal(expectedProblem, Assert.IsType<BagGoalResult.GoalBreaksRules>(result).Problem);
        Assert.Equal(100_000, holidays.TargetAmountInCents);
        Assert.Null(holidays.TargetDate);
    }

    // DD §7.1 : la date est vérifiée à chaque saisie. Une date passée, gardée telle quelle, est refusée : il faut en choisir une autre.
    [Fact]
    public async Task DateThatHasPassedIsRefusedEvenWhenItIsUnchanged()
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");
        var passedDate = services.Today.AddDays(3);
        holidays.ReplaceGoal(100_000, passedDate, services.Today);
        services.Clock.Advance(TimeSpan.FromDays(5));

        var result = await services.CreateGoal().ReplaceAsync(services.UserId, holidays.Id, 150_000, passedDate);

        Assert.Equal(BagGoalProblem.DateTooEarly, Assert.IsType<BagGoalResult.GoalBreaksRules>(result).Problem);
        Assert.Equal(100_000, holidays.TargetAmountInCents);
    }

    // Le jour de la saisie est celui de Paris : à 0 h 30 à Paris, il est encore 23 h 30 la veille en temps universel.
    [Fact]
    public async Task DayOfTheEntryIsTheDayInParis()
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");
        services.Clock.Advance(new TimeSpan(11, 30, 0));
        var goal = services.CreateGoal();

        var refused = await goal.ReplaceAsync(services.UserId, holidays.Id, 500, new DateOnly(2026, 11, 5));
        var accepted = await goal.ReplaceAsync(services.UserId, holidays.Id, 500, new DateOnly(2026, 11, 6));

        Assert.IsType<BagGoalResult.GoalBreaksRules>(refused);
        Assert.IsType<BagGoalResult.Changed>(accepted);
    }

    // SS §4 : le sac d'un autre utilisateur n'est pas trouvé, comme un sac qui n'existe pas.
    [Fact]
    public async Task BagOfAnotherUserIsNotFound()
    {
        var services = new BagServices();
        var otherUserBag = services.SaveBag("Sac d'un autre", Guid.NewGuid());

        var result = await services.CreateGoal().ReplaceAsync(services.UserId, otherUserBag.Id, 100_000, null);

        Assert.IsType<BagGoalResult.NotFound>(result);
        Assert.Null(otherUserBag.TargetAmountInCents);
    }

    // Après 10 conflits, rien n'est gardé : le sac reprend l'objectif qu'il avait à sa lecture.
    [Fact]
    public async Task GoalIsKeptAfterTenConflicts()
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");
        holidays.ReplaceGoal(100_000, null, services.Today);
        services.Repository.Conflicts = 10;

        var result = await services.CreateGoal().ReplaceAsync(services.UserId, holidays.Id, 200_000, null);

        Assert.IsType<BagGoalResult.NotSaved>(result);
        Assert.Equal(100_000, holidays.TargetAmountInCents);
    }
}
