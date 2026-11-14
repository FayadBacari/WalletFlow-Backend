using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Tests.Domain;

// Vérifie qu'un sac ne change que par ses propres règles (GR Sacs, DD §7.1).
public class BagTests
{
    private static readonly DateOnly Today = new DateOnly(2026, 11, 12);

    // Crée un sac avec un objectif de 1 000 € pour le 30/06/2027.
    private static Bag CreateBagWithGoal()
    {
        var bag = new Bag { Name = "Vacances", UserId = Guid.NewGuid() };
        bag.ReplaceGoal(100_000, new DateOnly(2027, 6, 30), Today);

        return bag;
    }

    [Fact]
    public void RenameGivesTheNewName()
    {
        var bag = CreateBagWithGoal();

        bag.Rename("Voyage");

        Assert.Equal("Voyage", bag.Name);
    }

    // Un nom qui n'a pas sa forme enregistrée, ou qui ne respecte pas les règles, est refusé, et le nom ne change pas.
    [Theory]
    [InlineData(" Voyage")]
    [InlineData("")]
    public void NameThatBreaksTheRulesIsRefused(string newName)
    {
        var bag = CreateBagWithGoal();

        Assert.Throws<ArgumentException>(() => bag.Rename(newName));
        Assert.Equal("Vacances", bag.Name);
    }

    [Fact]
    public void TooLongNameIsRefused()
    {
        var bag = CreateBagWithGoal();

        Assert.Throws<ArgumentException>(() => bag.Rename(new string('a', 51)));
        Assert.Equal("Vacances", bag.Name);
    }

    [Fact]
    public void GoalIsChangedThenRemoved()
    {
        var bag = CreateBagWithGoal();

        bag.ReplaceGoal(250_000, null, Today);
        Assert.Equal(250_000, bag.TargetAmountInCents);
        Assert.Null(bag.TargetDate);

        bag.ReplaceGoal(null, null, Today);
        Assert.Null(bag.TargetAmountInCents);
        Assert.Null(bag.TargetDate);
    }

    // Un objectif refusé ne change rien : ni le montant, ni la date.
    [Theory]
    [InlineData(0, "2027-01-01")]
    [InlineData(1_000_000_001, null)]
    [InlineData(null, "2027-01-01")]
    [InlineData(500, "2026-11-12")]
    public void GoalThatBreaksTheRulesIsRefusedAndTheOldOneStays(int? targetAmountInCents, string? targetDate)
    {
        var bag = CreateBagWithGoal();
        DateOnly? date = targetDate == null ? null : DateOnly.Parse(targetDate);

        Assert.Throws<ArgumentException>(() => bag.ReplaceGoal(targetAmountInCents, date, Today));
        Assert.Equal(100_000, bag.TargetAmountInCents);
        Assert.Equal(new DateOnly(2027, 6, 30), bag.TargetDate);
    }
}
