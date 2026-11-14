using WalletFlow.Application.Bags;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Tests.Application;

// Vérifie les déplacements d'argent (GR Sacs, SS §4, §5 et §6, DD §7.2).
public class BagMovementServiceTests
{
    // --- Les trois sortes de déplacement

    [Fact]
    public async Task AvailableMoneyGoesIntoABag()
    {
        var services = new BagServices();
        services.AddBankAccount(100_000);
        var holidays = services.SaveBag("Vacances");

        var result = await services.CreateMovement().MoveAsync(services.UserId, null, holidays.Id, 30_000);

        var moved = Assert.IsType<BagMovementResult.Moved>(result);
        var savedMovement = Assert.Single(services.Repository.SavedMovements);
        Assert.Same(savedMovement, moved.Movement);
        Assert.Null(savedMovement.SourceBagId);
        Assert.Equal(holidays.Id, savedMovement.DestinationBagId);
        Assert.Equal(30_000, savedMovement.AmountInCents);
        Assert.Equal(services.Now, savedMovement.MovedAt);
        Assert.Equal(30_000, services.BalanceOf(holidays));
        Assert.Contains(services.User, services.Repository.CommittedUserChecks);
    }

    [Fact]
    public async Task MoneyGoesFromOneBagToAnother()
    {
        var services = new BagServices();
        services.AddBankAccount(100_000);
        var holidays = services.SaveBag("Vacances");
        var savings = services.SaveBag("Épargne");
        services.SaveMovement(null, holidays, 50_000);

        var result = await services.CreateMovement().MoveAsync(services.UserId, holidays.Id, savings.Id, 20_000);

        Assert.IsType<BagMovementResult.Moved>(result);
        Assert.Equal(30_000, services.BalanceOf(holidays));
        Assert.Equal(20_000, services.BalanceOf(savings));
    }

    [Fact]
    public async Task MoneyGoesBackToTheAvailableMoney()
    {
        var services = new BagServices();
        services.AddBankAccount(100_000);
        var holidays = services.SaveBag("Vacances");
        services.SaveMovement(null, holidays, 50_000);

        var result = await services.CreateMovement().MoveAsync(services.UserId, holidays.Id, null, 50_000);

        Assert.IsType<BagMovementResult.Moved>(result);
        Assert.Equal(0, services.BalanceOf(holidays));
        Assert.Null(services.Repository.SavedMovements.Last().DestinationBagId);
    }

    // --- Requêtes refusées avant toute lecture

    [Fact]
    public async Task MovementWithoutAnyBagIsRefused()
    {
        var services = new BagServices();
        services.AddBankAccount(100_000);

        var result = await services.CreateMovement().MoveAsync(services.UserId, null, null, 1000);

        Assert.IsType<BagMovementResult.NoBag>(result);
        Assert.Empty(services.Repository.SavedMovements);
    }

    [Fact]
    public async Task MovementFromABagToItselfIsRefused()
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");
        services.SaveMovement(null, holidays, 50_000);

        var result = await services.CreateMovement().MoveAsync(services.UserId, holidays.Id, holidays.Id, 1000);

        Assert.IsType<BagMovementResult.SameBag>(result);
        Assert.Single(services.Repository.SavedMovements);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    [InlineData(1_000_000_001)]
    [InlineData(int.MinValue)]
    public async Task AmountOutsideTheLimitsIsRefused(int amountInCents)
    {
        var services = new BagServices();
        services.AddBankAccount(100_000);
        var holidays = services.SaveBag("Vacances");

        var result = await services.CreateMovement().MoveAsync(services.UserId, null, holidays.Id, amountInCents);

        Assert.IsType<BagMovementResult.AmountBreaksRules>(result);
        Assert.Empty(services.Repository.SavedMovements);
        Assert.Equal(0, services.UnitOfWork.CommittedTransactionCount);
    }

    // --- SS §4 : les deux sacs doivent être à l'utilisateur

    [Fact]
    public async Task BagOfAnotherUserIsNotFoundOnEitherSide()
    {
        var services = new BagServices();
        services.AddBankAccount(100_000);
        var holidays = services.SaveBag("Vacances");
        services.SaveMovement(null, holidays, 50_000);
        var otherUserBag = services.SaveBag("Sac d'un autre", Guid.NewGuid());
        services.SaveMovement(null, otherUserBag, 50_000);
        var movement = services.CreateMovement();

        var intoOtherBag = await movement.MoveAsync(services.UserId, holidays.Id, otherUserBag.Id, 1000);
        var fromOtherBag = await movement.MoveAsync(services.UserId, otherUserBag.Id, holidays.Id, 1000);
        var withdrawFromOtherBag = await movement.MoveAsync(services.UserId, otherUserBag.Id, null, 1000);
        var unknownBag = await movement.MoveAsync(services.UserId, null, Guid.NewGuid(), 1000);

        Assert.IsType<BagMovementResult.NotFound>(intoOtherBag);
        Assert.IsType<BagMovementResult.NotFound>(fromOtherBag);
        Assert.IsType<BagMovementResult.NotFound>(withdrawFromOtherBag);
        Assert.IsType<BagMovementResult.NotFound>(unknownBag);
        Assert.Equal(2, services.Repository.SavedMovements.Count);
        Assert.Equal(50_000, services.BalanceOf(otherUserBag));
    }

    // --- GR Sacs : un sac ne devient jamais négatif

    [Fact]
    public async Task BagCanBeEmptiedButNotMadeNegative()
    {
        var services = new BagServices();
        services.AddBankAccount(100_000);
        var holidays = services.SaveBag("Vacances");
        var savings = services.SaveBag("Épargne");
        services.SaveMovement(null, holidays, 1000);
        var movement = services.CreateMovement();

        var tooMuch = await movement.MoveAsync(services.UserId, holidays.Id, savings.Id, 1001);
        var tooMuchToAvailable = await movement.MoveAsync(services.UserId, holidays.Id, null, 1001);
        var everything = await movement.MoveAsync(services.UserId, holidays.Id, savings.Id, 1000);
        var fromEmptyBag = await movement.MoveAsync(services.UserId, holidays.Id, null, 1);

        Assert.IsType<BagMovementResult.SourceBagTooLow>(tooMuch);
        Assert.IsType<BagMovementResult.SourceBagTooLow>(tooMuchToAvailable);
        Assert.IsType<BagMovementResult.Moved>(everything);
        Assert.IsType<BagMovementResult.SourceBagTooLow>(fromEmptyBag);
        Assert.Equal(0, services.BalanceOf(holidays));
        Assert.Equal(1000, services.BalanceOf(savings));
    }

    // --- GR Sacs : le total des sacs ne passe jamais au-dessus du solde total

    [Fact]
    public async Task BagsCanHoldTheWholeBalanceButNotOneCentMore()
    {
        var services = new BagServices();
        services.AddBankAccount(100_000);
        var holidays = services.SaveBag("Vacances");
        var savings = services.SaveBag("Épargne");
        services.SaveMovement(null, holidays, 70_000);
        var movement = services.CreateMovement();

        var oneCentTooMuch = await movement.MoveAsync(services.UserId, null, savings.Id, 30_001);
        var exactlyTheRest = await movement.MoveAsync(services.UserId, null, savings.Id, 30_000);
        var oneMoreCent = await movement.MoveAsync(services.UserId, null, holidays.Id, 1);

        Assert.IsType<BagMovementResult.AvailableMoneyTooLow>(oneCentTooMuch);
        Assert.IsType<BagMovementResult.Moved>(exactlyTheRest);
        Assert.IsType<BagMovementResult.AvailableMoneyTooLow>(oneMoreCent);
        Assert.Equal(100_000, services.BalanceOf(holidays) + services.BalanceOf(savings));
    }

    // Le solde total est celui de tous les comptes de l'utilisateur, et seulement les siens.
    [Fact]
    public async Task TotalBalanceAddsAllTheAccountsOfTheUserOnly()
    {
        var services = new BagServices();
        services.AddBankAccount(60_000);
        services.AddBankAccount(40_000);
        services.AddBankAccount(500_000, Guid.NewGuid());
        var holidays = services.SaveBag("Vacances");
        var movement = services.CreateMovement();

        var tooMuch = await movement.MoveAsync(services.UserId, null, holidays.Id, 100_001);
        var whole = await movement.MoveAsync(services.UserId, null, holidays.Id, 100_000);

        Assert.IsType<BagMovementResult.AvailableMoneyTooLow>(tooMuch);
        Assert.IsType<BagMovementResult.Moved>(whole);
    }

    // Les sacs d'un autre utilisateur ne prennent rien à l'argent disponible de celui-ci.
    [Fact]
    public async Task BagsOfAnotherUserDoNotCount()
    {
        var services = new BagServices();
        services.AddBankAccount(100_000);
        var holidays = services.SaveBag("Vacances");
        var otherUserBag = services.SaveBag("Sac d'un autre", Guid.NewGuid());
        services.SaveMovement(null, otherUserBag, 90_000);

        var result = await services.CreateMovement().MoveAsync(services.UserId, null, holidays.Id, 100_000);

        Assert.IsType<BagMovementResult.Moved>(result);
    }

    // Le solde a baissé sous le total des sacs : ajouter de l'argent disponible est refusé,
    // mais déplacer entre deux sacs ou retirer vers l'argent disponible reste permis.
    [Fact]
    public async Task BagsAboveTheTotalBalanceCanStillBeEmptiedOrRearranged()
    {
        var services = new BagServices();
        var account = services.AddBankAccount(100_000);
        var holidays = services.SaveBag("Vacances");
        var savings = services.SaveBag("Épargne");
        services.SaveMovement(null, holidays, 100_000);
        account.UpdateBalance(80_000, services.Now);
        var movement = services.CreateMovement();

        var addition = await movement.MoveAsync(services.UserId, null, savings.Id, 1);
        var rearrangement = await movement.MoveAsync(services.UserId, holidays.Id, savings.Id, 50_000);
        var withdrawal = await movement.MoveAsync(services.UserId, holidays.Id, null, 10_000);

        Assert.IsType<BagMovementResult.AvailableMoneyTooLow>(addition);
        Assert.IsType<BagMovementResult.Moved>(rearrangement);
        Assert.IsType<BagMovementResult.Moved>(withdrawal);
        Assert.Equal(40_000, services.BalanceOf(holidays));
        Assert.Equal(50_000, services.BalanceOf(savings));
    }

    // Un solde total négatif, ou l'absence de compte, ne laisse rien mettre dans un sac.
    [Fact]
    public async Task NegativeOrMissingTotalBalanceLetsNothingIntoABag()
    {
        var overdrawn = new BagServices();
        overdrawn.AddBankAccount(-5000);
        var overdrawnBag = overdrawn.SaveBag("Vacances");
        var withoutAccount = new BagServices();
        var emptyBag = withoutAccount.SaveBag("Vacances");

        var fromOverdrawn = await overdrawn.CreateMovement().MoveAsync(overdrawn.UserId, null, overdrawnBag.Id, 1);
        var fromNothing = await withoutAccount.CreateMovement().MoveAsync(withoutAccount.UserId, null, emptyBag.Id, 1);

        Assert.IsType<BagMovementResult.AvailableMoneyTooLow>(fromOverdrawn);
        Assert.IsType<BagMovementResult.AvailableMoneyTooLow>(fromNothing);
    }

    // --- SS §6 : 1 000 déplacements au plus sur les dernières 24 heures

    [Fact]
    public async Task ThousandthMovementPassesButNotTheNextOne()
    {
        var services = new BagServices();
        services.AddBankAccount(1_000_000);
        var holidays = services.SaveBag("Vacances");
        services.SaveMovement(null, holidays, 100_000, services.Now.AddHours(-30));

        for (var number = 1; number <= 999; number++)
        {
            services.SaveMovement(null, holidays, 1);
        }

        var thousandth = await services.CreateMovement().MoveAsync(services.UserId, holidays.Id, null, 1);
        var next = await services.CreateMovement().MoveAsync(services.UserId, holidays.Id, null, 1);

        Assert.IsType<BagMovementResult.Moved>(thousandth);
        Assert.IsType<BagMovementResult.TooManyMovements>(next);
        Assert.Equal(1001, services.Repository.SavedMovements.Count);
    }

    // Un mouvement fait il y a exactement 24 heures compte encore ; un instant plus tôt, il ne compte plus.
    [Fact]
    public async Task MovementsAreCountedOverTheLastTwentyFourHours()
    {
        var services = new BagServices();
        services.AddBankAccount(1_000_000);
        var holidays = services.SaveBag("Vacances");
        var dayStart = services.Now - BagRules.MovementDay;

        for (var number = 1; number <= 1000; number++)
        {
            services.SaveMovement(null, holidays, 1, dayStart);
        }

        var stillCounted = await services.CreateMovement().MoveAsync(services.UserId, holidays.Id, null, 1);
        services.Clock.Advance(TimeSpan.FromTicks(1));
        var noLongerCounted = await services.CreateMovement().MoveAsync(services.UserId, holidays.Id, null, 1);

        Assert.IsType<BagMovementResult.TooManyMovements>(stillCounted);
        Assert.IsType<BagMovementResult.Moved>(noLongerCounted);
    }

    // Les déplacements d'un autre utilisateur ne comptent pas dans la limite de celui-ci.
    [Fact]
    public async Task MovementsOfAnotherUserDoNotCount()
    {
        var services = new BagServices();
        services.AddBankAccount(1_000_000);
        var holidays = services.SaveBag("Vacances");
        var otherUserBag = services.SaveBag("Sac d'un autre", Guid.NewGuid());

        for (var number = 1; number <= 1000; number++)
        {
            services.SaveMovement(null, otherUserBag, 1);
        }

        var result = await services.CreateMovement().MoveAsync(services.UserId, null, holidays.Id, 1);

        Assert.IsType<BagMovementResult.Moved>(result);
    }

    // --- Requêtes simultanées (SS §5)

    // Une autre requête remplit un autre sac juste avant cet enregistrement : le passage suivant recompte l'argent disponible.
    [Fact]
    public async Task AdditionAtTheSameMomentIsCountedBeforeTheNextTry()
    {
        var services = new BagServices();
        services.AddBankAccount(100_000);
        var holidays = services.SaveBag("Vacances");
        var savings = services.SaveBag("Épargne");
        services.Repository.MovementSavedBeforeSave = new BagMovement
        {
            AmountInCents = 50_000,
            MovedAt = services.Now,
            DestinationBagId = savings.Id
        };

        var result = await services.CreateMovement().MoveAsync(services.UserId, null, holidays.Id, 60_000);

        Assert.IsType<BagMovementResult.AvailableMoneyTooLow>(result);
        Assert.Equal(0, services.BalanceOf(holidays));
        Assert.Equal(50_000, services.BalanceOf(savings));
    }

    // SS §5 : une autre requête remplit un autre sac juste après la lecture des soldes, avant la lecture de la ligne de
    // l'utilisateur si elle était faite après. La ligne est lue en premier : sa version a changé à l'enregistrement,
    // et le passage suivant recompte. Lue après les soldes, elle aurait déjà sa nouvelle version, et l'ajout passerait.
    [Fact]
    public async Task UserRowIsReadBeforeTheBalances()
    {
        var services = new BagServices();
        services.AddBankAccount(100_000);
        var holidays = services.SaveBag("Vacances");
        var savings = services.SaveBag("Épargne");
        services.Repository.MovementSavedAfterBalancesRead = new BagMovement
        {
            AmountInCents = 50_000,
            MovedAt = services.Now,
            DestinationBagId = savings.Id
        };

        var result = await services.CreateMovement().MoveAsync(services.UserId, null, holidays.Id, 60_000);

        Assert.IsType<BagMovementResult.AvailableMoneyTooLow>(result);
        Assert.Equal(0, services.BalanceOf(holidays));
        Assert.Equal(50_000, services.BalanceOf(savings));
    }

    // Une autre requête vide en partie le même sac juste avant cet enregistrement : le passage suivant relit son solde.
    [Fact]
    public async Task WithdrawalAtTheSameMomentIsCountedBeforeTheNextTry()
    {
        var services = new BagServices();
        services.AddBankAccount(100_000);
        var holidays = services.SaveBag("Vacances");
        services.SaveMovement(null, holidays, 1000);
        services.Repository.MovementSavedBeforeSave = new BagMovement
        {
            AmountInCents = 500,
            MovedAt = services.Now,
            SourceBagId = holidays.Id
        };

        var result = await services.CreateMovement().MoveAsync(services.UserId, holidays.Id, null, 800);

        Assert.IsType<BagMovementResult.SourceBagTooLow>(result);
        Assert.Equal(500, services.BalanceOf(holidays));
    }

    // Une autre requête supprime le sac d'arrivée juste avant cet enregistrement : le passage suivant ne le trouve plus.
    [Fact]
    public async Task BagDeletedAtTheSameMomentIsNotFoundAtTheNextTry()
    {
        var services = new BagServices();
        services.AddBankAccount(100_000);
        var holidays = services.SaveBag("Vacances");
        services.Repository.BagDeletedBeforeSave = holidays.Id;

        var result = await services.CreateMovement().MoveAsync(services.UserId, null, holidays.Id, 1000);

        Assert.IsType<BagMovementResult.NotFound>(result);
        Assert.Empty(services.Repository.SavedMovements);
    }

    // Après 10 conflits, le déplacement est refusé, jamais fait sans contrôle. Au dixième essai, il passe encore.
    [Fact]
    public async Task MovementIsRefusedOnlyAfterTenConflicts()
    {
        var services = new BagServices();
        services.AddBankAccount(100_000);
        var holidays = services.SaveBag("Vacances");
        services.Repository.Conflicts = 10;

        var refused = await services.CreateMovement().MoveAsync(services.UserId, null, holidays.Id, 1000);
        services.Repository.Conflicts = 9;
        var moved = await services.CreateMovement().MoveAsync(services.UserId, null, holidays.Id, 1000);

        Assert.IsType<BagMovementResult.NotSaved>(refused);
        Assert.IsType<BagMovementResult.Moved>(moved);
        Assert.Single(services.Repository.SavedMovements);
    }

    [Fact]
    public async Task AccountThatNoLongerExistsMovesNothing()
    {
        var services = new BagServices();
        services.AddBankAccount(100_000);
        var holidays = services.SaveBag("Vacances");
        services.Repository.SavedUsers.Clear();

        var result = await services.CreateMovement().MoveAsync(services.UserId, null, holidays.Id, 1000);

        Assert.IsType<BagMovementResult.NotSaved>(result);
        Assert.Empty(services.Repository.SavedMovements);
    }
}
