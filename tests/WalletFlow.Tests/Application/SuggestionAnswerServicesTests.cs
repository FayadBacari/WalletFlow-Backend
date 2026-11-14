using WalletFlow.Application.Suggestions;
using WalletFlow.Domain.BagsAndSuggestions;
using static WalletFlow.Tests.Application.SuggestionServices;

namespace WalletFlow.Tests.Application;

// Vérifie l'acceptation et l'ignorance d'une suggestion (GR Sacs, SS §4 et §5, DD §7.2 et §8), avec une fausse base.
// L'utilisateur a 1 000 € sur son compte, un sac « Épargne » vide, et une suggestion de 300 € proposée pour octobre.
public class SuggestionAnswerServicesTests
{
    private readonly SuggestionServices services = new SuggestionServices();
    private readonly Bag savingsBag;
    private readonly SavingsSuggestion suggestion;

    // Prépare le compte, le sac et la suggestion.
    public SuggestionAnswerServicesTests()
    {
        services.AddBankAccount(100_000);
        savingsBag = services.SaveBag("Épargne");
        suggestion = services.SaveSuggestion(SuggestionRule.UnallocatedMoney, October, 30_000);
    }

    // Accepte la suggestion des tests en plaçant ce montant dans ce sac.
    private async Task<SuggestionAcceptanceResult> AcceptAsync(int amountInCents, Guid? bagId = null, Guid? suggestionId = null)
    {
        return await services.CreateAcceptance().AcceptAsync(
            services.UserId,
            suggestionId ?? suggestion.Id,
            bagId ?? savingsBag.Id,
            amountInCents);
    }

    // Ignore la suggestion des tests, ou celle-ci.
    private async Task<SuggestionDismissalResult> DismissAsync(Guid? suggestionId = null)
    {
        return await services.CreateDismissal().DismissAsync(services.UserId, suggestionId ?? suggestion.Id);
    }

    // Les mouvements gardés de l'utilisateur.
    private List<BagMovement> SavedMovements => services.Bags.SavedMovements;

    // Accepter place le montant de l'argent disponible vers le sac, lie le mouvement à la suggestion, et la date.
    [Fact]
    public async Task AcceptingPlacesTheAmountInTheChosenBag()
    {
        var result = await AcceptAsync(30_000);

        var movement = Assert.IsType<SuggestionAcceptanceResult.Accepted>(result).Movement;
        Assert.Equal(movement, Assert.Single(SavedMovements));
        Assert.Null(movement.SourceBagId);
        Assert.Equal(savingsBag.Id, movement.DestinationBagId);
        Assert.Equal(30_000, movement.AmountInCents);
        Assert.Equal(suggestion.Id, movement.SavingsSuggestionId);
        Assert.Equal(services.Now, movement.MovedAt);
        Assert.Equal(SuggestionStatus.Accepted, suggestion.Status);
        Assert.Equal(services.Now, suggestion.StatusChangedAt);
        Assert.Equal(30_000, (await services.CreateList().ListAsync(services.UserId)).SavedInCents);
    }

    // Un montant plus petit est permis ; un montant plus grand que la suggestion, ou de moins d'un centime, est refusé.
    [Theory]
    [InlineData(1, true)]
    [InlineData(29_999, true)]
    [InlineData(30_001, false)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    public async Task AmountGoesFromOneCentToTheSuggestion(int amountInCents, bool isAccepted)
    {
        var result = await AcceptAsync(amountInCents);

        if (isAccepted)
        {
            Assert.IsType<SuggestionAcceptanceResult.Accepted>(result);
            return;
        }

        Assert.IsType<SuggestionAcceptanceResult.AmountBreaksRules>(result);
        Assert.Empty(SavedMovements);
        Assert.Equal(SuggestionStatus.Proposed, suggestion.Status);
    }

    // SS §4 : la suggestion d'un autre, une suggestion inconnue, ou une suggestion retirée avec sa catégorie n'est jamais trouvée.
    [Fact]
    public async Task SuggestionOfAnotherOrWithdrawnIsNotFound()
    {
        var ofOther = services.SaveSuggestion(SuggestionRule.UnallocatedMoney, October, 30_000, userId: Guid.NewGuid());
        var withdrawn = services.SaveSuggestion(SuggestionRule.CategoryOverrun, October, 30_000);

        foreach (var suggestionId in new[] { ofOther.Id, withdrawn.Id, Guid.NewGuid() })
        {
            Assert.IsType<SuggestionAcceptanceResult.NotFound>(await AcceptAsync(1_000, suggestionId: suggestionId));
            Assert.IsType<SuggestionDismissalResult.NotFound>(await DismissAsync(suggestionId));
        }

        Assert.Empty(SavedMovements);
        Assert.Equal(SuggestionStatus.Proposed, ofOther.Status);
        Assert.Equal(SuggestionStatus.Proposed, withdrawn.Status);
    }

    // SS §4 : le sac d'un autre utilisateur n'est jamais trouvé, et la suggestion reste proposée.
    [Fact]
    public async Task BagOfAnotherIsNotFound()
    {
        var bagOfOther = services.SaveBag("Épargne", Guid.NewGuid());

        var result = await AcceptAsync(1_000, bagOfOther.Id);

        Assert.IsType<SuggestionAcceptanceResult.BagNotFound>(result);
        Assert.Empty(SavedMovements);
        Assert.Equal(SuggestionStatus.Proposed, suggestion.Status);
    }

    // GR Sacs : le montant ne peut pas porter le total des sacs au-dessus du solde total. Avec 800 € déjà dans les sacs,
    // il reste 200 € : 300 € sont refusés, 200 € sont placés.
    [Fact]
    public async Task AvailableMoneyLimitsTheAmount()
    {
        SavedMovements.Add(new BagMovement { AmountInCents = 80_000, MovedAt = services.Now, DestinationBagId = services.SaveBag("Vacances").Id });

        Assert.IsType<SuggestionAcceptanceResult.AvailableMoneyTooLow>(await AcceptAsync(30_000));
        Assert.Equal(SuggestionStatus.Proposed, suggestion.Status);
        Assert.IsType<SuggestionAcceptanceResult.Accepted>(await AcceptAsync(20_000));
    }

    // SS §6 : l'acceptation compte parmi les 1 000 déplacements des dernières 24 heures.
    [Fact]
    public async Task AcceptanceCountsAmongTheDailyMovements()
    {
        var otherBag = services.SaveBag("Vacances");
        for (var index = 0; index < BagRules.MaximumMovementsPerDay; index++)
        {
            SavedMovements.Add(new BagMovement { AmountInCents = 1, MovedAt = services.Now.AddHours(-23), DestinationBagId = otherBag.Id });
        }

        Assert.IsType<SuggestionAcceptanceResult.TooManyMovements>(await AcceptAsync(1_000));
        Assert.Equal(SuggestionStatus.Proposed, suggestion.Status);
    }

    // Une suggestion acceptée ou ignorée ne change plus.
    [Fact]
    public async Task AnsweredSuggestionNeverChanges()
    {
        Assert.IsType<SuggestionAcceptanceResult.Accepted>(await AcceptAsync(30_000));

        Assert.IsType<SuggestionAcceptanceResult.AlreadyAnswered>(await AcceptAsync(30_000));
        Assert.IsType<SuggestionDismissalResult.AlreadyAnswered>(await DismissAsync());
        Assert.Single(SavedMovements);
        Assert.Equal(SuggestionStatus.Accepted, suggestion.Status);
    }

    // Ignorer date la réponse, ne déplace rien, et la suggestion ne peut plus être acceptée.
    [Fact]
    public async Task DismissingDatesTheAnswerWithoutMovingMoney()
    {
        Assert.IsType<SuggestionDismissalResult.Dismissed>(await DismissAsync());

        Assert.Equal(SuggestionStatus.Ignored, suggestion.Status);
        Assert.Equal(services.Now, suggestion.StatusChangedAt);
        Assert.Empty(SavedMovements);
        Assert.IsType<SuggestionAcceptanceResult.AlreadyAnswered>(await AcceptAsync(30_000));
        Assert.IsType<SuggestionDismissalResult.AlreadyAnswered>(await DismissAsync());
    }

    // SS §5 : un second clic sur « accepter » enregistré juste avant celui-ci ne laisse qu'un seul mouvement.
    [Fact]
    public async Task DoubleClickCreatesOneMovement()
    {
        services.Suggestions.OtherRequestBeforeSave = () =>
        {
            suggestion.Accept(services.Now);
            SavedMovements.Add(new BagMovement { AmountInCents = 30_000, MovedAt = services.Now, DestinationBagId = savingsBag.Id, SavingsSuggestionId = suggestion.Id });
        };

        var result = await AcceptAsync(30_000);

        Assert.IsType<SuggestionAcceptanceResult.AlreadyAnswered>(result);
        Assert.Single(SavedMovements);
    }

    // « Ignorer » enregistré juste avant « accepter » l'emporte : aucun argent ne bouge. L'inverse aussi.
    [Fact]
    public async Task OnlyOneOfAcceptAndIgnoreWins()
    {
        services.Suggestions.OtherRequestBeforeSave = () => suggestion.Ignore(services.Now);

        Assert.IsType<SuggestionAcceptanceResult.AlreadyAnswered>(await AcceptAsync(30_000));
        Assert.Empty(SavedMovements);
        Assert.Equal(SuggestionStatus.Ignored, suggestion.Status);

        var other = services.SaveSuggestion(SuggestionRule.InsufficientSavings, October, 5_000);
        services.Suggestions.OtherRequestBeforeSave = () => other.Accept(services.Now);

        Assert.IsType<SuggestionDismissalResult.AlreadyAnswered>(await DismissAsync(other.Id));
        Assert.Equal(SuggestionStatus.Accepted, other.Status);
    }

    // Un autre déplacement enregistré juste après la lecture des soldes : l'acceptation refait ses contrôles sur les soldes
    // à jour, et refuse le montant qui ne tient plus sous le solde total (SS §5).
    [Fact]
    public async Task MovementSavedMeanwhileIsCheckedAgain()
    {
        services.Bags.MovementSavedAfterBalancesRead = new BagMovement
        {
            AmountInCents = 80_000,
            MovedAt = services.Now,
            DestinationBagId = services.SaveBag("Vacances").Id
        };

        Assert.IsType<SuggestionAcceptanceResult.AvailableMoneyTooLow>(await AcceptAsync(30_000));
        Assert.Single(SavedMovements);
        Assert.Equal(SuggestionStatus.Proposed, suggestion.Status);
    }

    // Des conflits qui ne cessent pas : rien n'est enregistré, et la suggestion reste proposée, sans date.
    [Fact]
    public async Task EndlessConflictsSaveNothing()
    {
        services.Suggestions.Conflicts = 100;

        Assert.IsType<SuggestionAcceptanceResult.NotSaved>(await AcceptAsync(30_000));
        Assert.IsType<SuggestionDismissalResult.NotSaved>(await DismissAsync());
        Assert.Empty(SavedMovements);
        Assert.Equal(SuggestionStatus.Proposed, suggestion.Status);
        Assert.Null(suggestion.StatusChangedAt);
    }
}
