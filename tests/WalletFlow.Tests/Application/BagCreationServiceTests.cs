using WalletFlow.Application.Bags;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Tests.Application;

// Vérifie la création d'un sac (GR Sacs, SS §6, DD §7.1).
public class BagCreationServiceTests
{
    [Fact]
    public async Task BagIsCreatedEmptyWithTheCleanedName()
    {
        var services = new BagServices();

        var result = await services.CreateCreation().CreateAsync(services.UserId, "  Vacances ", null, null);

        var created = Assert.IsType<BagCreationResult.Created>(result);
        var savedBag = Assert.Single(services.Repository.SavedBags);
        Assert.Equal("Vacances", savedBag.Name);
        Assert.Equal(services.UserId, savedBag.UserId);
        Assert.Null(savedBag.TargetAmountInCents);
        Assert.Equal(savedBag.Id, created.Bag.Id);
        Assert.Equal(0, created.Bag.BalanceInCents);
        Assert.Null(created.Bag.ProgressPercent);
        Assert.Contains(services.User, services.Repository.CommittedUserChecks);
    }

    [Fact]
    public async Task BagIsCreatedWithItsGoal()
    {
        var services = new BagServices();
        var tomorrow = services.Today.AddDays(1);

        var result = await services.CreateCreation().CreateAsync(services.UserId, "Vacances", 150_000, tomorrow);

        var created = Assert.IsType<BagCreationResult.Created>(result);
        var savedBag = Assert.Single(services.Repository.SavedBags);
        Assert.Equal(150_000, savedBag.TargetAmountInCents);
        Assert.Equal(tomorrow, savedBag.TargetDate);
        Assert.Equal(0, created.Bag.ProgressPercent);
    }

    // Un nom refusé ne crée rien, et rien n'est enregistré.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NameThatBreaksTheRulesCreatesNothing(string? typedName)
    {
        var services = new BagServices();

        var result = await services.CreateCreation().CreateAsync(services.UserId, typedName, null, null);

        Assert.IsType<BagCreationResult.NameBreaksRules>(result);
        Assert.Empty(services.Repository.SavedBags);
        Assert.Equal(0, services.UnitOfWork.CommittedTransactionCount);
    }

    [Fact]
    public async Task TooLongNameOrInvisibleCharacterCreatesNothing()
    {
        var services = new BagServices();
        var creation = services.CreateCreation();

        Assert.IsType<BagCreationResult.NameBreaksRules>(await creation.CreateAsync(services.UserId, new string('a', 51), null, null));
        Assert.IsType<BagCreationResult.NameBreaksRules>(await creation.CreateAsync(services.UserId, "Vac" + (char)0x200B + "ances", null, null));
        Assert.Empty(services.Repository.SavedBags);
    }

    // Un objectif refusé ne crée rien, et la raison est donnée.
    [Theory]
    [InlineData(0, null, BagGoalProblem.AmountBreaksRules)]
    [InlineData(1_000_000_001, null, BagGoalProblem.AmountBreaksRules)]
    [InlineData(null, 30, BagGoalProblem.DateWithoutAmount)]
    [InlineData(500, 0, BagGoalProblem.DateTooEarly)]
    [InlineData(500, -1, BagGoalProblem.DateTooEarly)]
    public async Task GoalThatBreaksTheRulesCreatesNothing(int? targetAmountInCents, int? daysFromToday, BagGoalProblem expectedProblem)
    {
        var services = new BagServices();
        DateOnly? targetDate = daysFromToday == null ? null : services.Today.AddDays(daysFromToday.Value);

        var result = await services.CreateCreation().CreateAsync(services.UserId, "Vacances", targetAmountInCents, targetDate);

        var refused = Assert.IsType<BagCreationResult.GoalBreaksRules>(result);
        Assert.Equal(expectedProblem, refused.Problem);
        Assert.Empty(services.Repository.SavedBags);
    }

    // Le jour de la saisie est celui de Paris : à 0 h 30 à Paris, il est encore 23 h 30 la veille en temps universel.
    [Fact]
    public async Task DayOfTheEntryIsTheDayInParis()
    {
        var services = new BagServices();
        services.Clock.Advance(new TimeSpan(11, 30, 0));
        var parisDay = new DateOnly(2026, 11, 5);
        var creation = services.CreateCreation();

        var refused = await creation.CreateAsync(services.UserId, "Vacances", 500, parisDay);
        var accepted = await creation.CreateAsync(services.UserId, "Vacances", 500, parisDay.AddDays(1));

        Assert.Equal(new DateTime(2026, 11, 4, 23, 30, 0, DateTimeKind.Utc), services.Now);
        Assert.Equal(BagGoalProblem.DateTooEarly, Assert.IsType<BagCreationResult.GoalBreaksRules>(refused).Problem);
        Assert.IsType<BagCreationResult.Created>(accepted);
    }

    [Fact]
    public async Task TwoBagsCanHaveTheSameName()
    {
        var services = new BagServices();
        var creation = services.CreateCreation();

        await creation.CreateAsync(services.UserId, "Vacances", null, null);
        var result = await creation.CreateAsync(services.UserId, "vacances", null, null);

        Assert.IsType<BagCreationResult.Created>(result);
        Assert.Equal(2, services.Repository.SavedBags.Count);
    }

    // SS §6 : 100 sacs au plus. Les sacs d'un autre utilisateur ne comptent pas.
    [Fact]
    public async Task HundredthBagIsCreatedButNotTheNextOne()
    {
        var services = new BagServices();
        services.SaveBag("Sac d'un autre", Guid.NewGuid());

        for (var number = 1; number <= 99; number++)
        {
            services.SaveBag("Sac " + number);
        }

        var hundredth = await services.CreateCreation().CreateAsync(services.UserId, "Sac 100", null, null);
        var next = await services.CreateCreation().CreateAsync(services.UserId, "Sac 101", null, null);

        Assert.IsType<BagCreationResult.Created>(hundredth);
        Assert.IsType<BagCreationResult.TooManyBags>(next);
        Assert.Equal(100, services.Repository.SavedBags.Count(bag => bag.UserId == services.UserId));
    }

    // Une autre requête crée le centième sac juste avant cet enregistrement : le passage suivant recompte et refuse.
    [Fact]
    public async Task CreationAtTheSameMomentIsCountedBeforeTheNextTry()
    {
        var services = new BagServices();

        for (var number = 1; number <= 99; number++)
        {
            services.SaveBag("Sac " + number);
        }

        services.Repository.BagCreatedBeforeSave = new Bag { Name = "Sac de l'autre requête", UserId = services.UserId };

        var result = await services.CreateCreation().CreateAsync(services.UserId, "Sac 100", null, null);

        Assert.IsType<BagCreationResult.TooManyBags>(result);
        Assert.Equal(100, services.Repository.SavedBags.Count);
        Assert.DoesNotContain(services.Repository.SavedBags, bag => bag.Name == "Sac 100");
    }

    // SS §6 : une autre requête crée le centième sac juste après le compte des sacs. La ligne de l'utilisateur est lue
    // en premier : sa version a changé à l'enregistrement, et le passage suivant recompte. Lue après le compte,
    // elle aurait déjà sa nouvelle version, et le cent unième sac passerait.
    [Fact]
    public async Task UserRowIsReadBeforeTheCount()
    {
        var services = new BagServices();

        for (var number = 1; number <= 99; number++)
        {
            services.SaveBag("Sac " + number);
        }

        services.Repository.BagCreatedAfterCount = new Bag { Name = "Sac de l'autre requête", UserId = services.UserId };

        var result = await services.CreateCreation().CreateAsync(services.UserId, "Sac 100", null, null);

        Assert.IsType<BagCreationResult.TooManyBags>(result);
        Assert.Equal(100, services.Repository.SavedBags.Count);
    }

    // Après 10 conflits, la création est refusée, jamais faite sans contrôle. Au dixième essai, elle passe encore.
    [Fact]
    public async Task CreationIsRefusedOnlyAfterTenConflicts()
    {
        var services = new BagServices();
        services.Repository.Conflicts = 10;

        var refused = await services.CreateCreation().CreateAsync(services.UserId, "Vacances", null, null);
        services.Repository.Conflicts = 9;
        var created = await services.CreateCreation().CreateAsync(services.UserId, "Vacances", null, null);

        Assert.IsType<BagCreationResult.NotSaved>(refused);
        Assert.IsType<BagCreationResult.Created>(created);
        Assert.Single(services.Repository.SavedBags);
    }

    [Fact]
    public async Task AccountThatNoLongerExistsCreatesNothing()
    {
        var services = new BagServices();
        services.Repository.SavedUsers.Clear();

        var result = await services.CreateCreation().CreateAsync(services.UserId, "Vacances", null, null);

        Assert.IsType<BagCreationResult.NotSaved>(result);
        Assert.Empty(services.Repository.SavedBags);
    }
}
