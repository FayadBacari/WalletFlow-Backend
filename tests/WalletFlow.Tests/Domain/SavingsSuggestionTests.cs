using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie qu'une suggestion ne reçoit qu'une seule réponse, et le repère du dernier mois calculé (GR Sacs, DD §3 et §8).
public class SavingsSuggestionTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    // Crée une suggestion proposée.
    private static SavingsSuggestion CreateProposed(SuggestionRule rule = SuggestionRule.UnallocatedMoney, Guid? categoryId = null)
    {
        return new SavingsSuggestion
        {
            Rule = rule,
            Month = new DateOnly(2026, 10, 1),
            MonthlySavingsInCents = 5_000,
            UserId = Guid.NewGuid(),
            CategoryId = categoryId
        };
    }

    [Fact]
    public void AcceptanceAndDismissalAreDated()
    {
        var accepted = CreateProposed();
        var ignored = CreateProposed();

        accepted.Accept(Now);
        ignored.Ignore(Now.AddMinutes(1));

        Assert.Equal(SuggestionStatus.Accepted, accepted.Status);
        Assert.Equal(Now, accepted.StatusChangedAt);
        Assert.Equal(SuggestionStatus.Ignored, ignored.Status);
        Assert.Equal(Now.AddMinutes(1), ignored.StatusChangedAt);
    }

    // Une suggestion acceptée ou ignorée ne change plus : une seconde réponse est refusée, et la première reste.
    [Fact]
    public void SecondAnswerIsRefused()
    {
        var accepted = CreateProposed();
        var ignored = CreateProposed();
        accepted.Accept(Now);
        ignored.Ignore(Now);

        Assert.Throws<InvalidOperationException>(() => accepted.Ignore(Now.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => accepted.Accept(Now.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => ignored.Accept(Now.AddDays(1)));
        Assert.Equal(SuggestionStatus.Accepted, accepted.Status);
        Assert.Equal(Now, accepted.StatusChangedAt);
        Assert.Equal(SuggestionStatus.Ignored, ignored.Status);
    }

    // Seule une suggestion de dépassement encore proposée et sans catégorie est retirée : sa catégorie a été supprimée.
    [Fact]
    public void OnlyAnUnansweredOverrunWithoutCategoryIsWithdrawn()
    {
        var withdrawn = CreateProposed(SuggestionRule.CategoryOverrun);
        var withCategory = CreateProposed(SuggestionRule.CategoryOverrun, Guid.NewGuid());
        var accepted = CreateProposed(SuggestionRule.CategoryOverrun);
        accepted.Accept(Now);

        Assert.True(withdrawn.IsWithdrawn());
        Assert.False(withCategory.IsWithdrawn());
        Assert.False(accepted.IsWithdrawn());
        Assert.False(CreateProposed(SuggestionRule.UnallocatedMoney).IsWithdrawn());
        Assert.False(CreateProposed(SuggestionRule.InsufficientSavings).IsWithdrawn());
    }

    // Le repère n'avance que vers un mois plus récent, toujours un premier jour de mois.
    [Fact]
    public void SuggestionMonthOnlyMovesForward()
    {
        var user = new User { Email = "repere@exemple.test", PasswordHash = "empreinte-de-test" };

        Assert.Null(user.LastSuggestionMonth);
        user.NoteSuggestionMonth(new DateOnly(2026, 9, 1));
        user.NoteSuggestionMonth(new DateOnly(2026, 10, 1));

        Assert.Equal(new DateOnly(2026, 10, 1), user.LastSuggestionMonth);
        Assert.Throws<ArgumentException>(() => user.NoteSuggestionMonth(new DateOnly(2026, 10, 1)));
        Assert.Throws<ArgumentException>(() => user.NoteSuggestionMonth(new DateOnly(2026, 9, 1)));
        Assert.Throws<ArgumentException>(() => user.NoteSuggestionMonth(new DateOnly(2026, 11, 15)));
        Assert.Equal(new DateOnly(2026, 10, 1), user.LastSuggestionMonth);
    }
}
