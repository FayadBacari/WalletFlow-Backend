using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Infrastructure.LocalDatabaseForTheTests;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la suppression d'un sac dans la vraie base (GR Sacs, MPD §7). La transaction est annulée à la fin.
public class BagDeletionTests
{
    // Les mouvements restent chargés en mémoire pendant la suppression : EF Core ne doit pas y toucher,
    // c'est la base qui applique ses règles (déclencheur et ON DELETE SET NULL).
    [Fact]
    public async Task BagWithItsHistoryLoadedIsDeletedAndTheDatabaseRulesApply()
    {
        await using var databaseContextOfThisBagTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisBagTest.Database.BeginTransactionAsync();

        var userOwningTheBagsOfThisTest = new User { Email = "test-sac-suppression@exemple.test", PasswordHash = "empreinte-de-test" };
        var bagThatIsDeletedByThisTest = new Bag { Name = "Sac supprimé", UserId = userOwningTheBagsOfThisTest.Id };
        var otherBagKeptByThisTest = new Bag { Name = "Sac conservé", UserId = userOwningTheBagsOfThisTest.Id };
        var movementComingFromTheAvailableMoney = new BagMovement { AmountInCents = 1000, DestinationBagId = bagThatIsDeletedByThisTest.Id };
        var movementBetweenTheTwoBagsOfThisTest = new BagMovement { AmountInCents = 500, SourceBagId = bagThatIsDeletedByThisTest.Id, DestinationBagId = otherBagKeptByThisTest.Id };
        databaseContextOfThisBagTest.Users.Add(userOwningTheBagsOfThisTest);
        databaseContextOfThisBagTest.Bags.AddRange(bagThatIsDeletedByThisTest, otherBagKeptByThisTest);
        databaseContextOfThisBagTest.BagMovements.AddRange(movementComingFromTheAvailableMoney, movementBetweenTheTwoBagsOfThisTest);
        await databaseContextOfThisBagTest.SaveChangesAsync();

        databaseContextOfThisBagTest.Bags.Remove(bagThatIsDeletedByThisTest);
        await databaseContextOfThisBagTest.SaveChangesAsync();

        // On oublie les objets en mémoire : tout ce qui suit est vraiment relu dans la base.
        databaseContextOfThisBagTest.ChangeTracker.Clear();

        Assert.False(await databaseContextOfThisBagTest.Bags.AnyAsync(bagSearchedInTheDatabaseTable => bagSearchedInTheDatabaseTable.Id == bagThatIsDeletedByThisTest.Id));

        // Le mouvement avec l'argent disponible disparaît, celui avec l'autre sac garde l'autre sac.
        Assert.False(await databaseContextOfThisBagTest.BagMovements.AnyAsync(movementSearchedInTheDatabaseTable => movementSearchedInTheDatabaseTable.Id == movementComingFromTheAvailableMoney.Id));
        var movementKeptInTheHistoryOfTheOtherBag = await databaseContextOfThisBagTest.BagMovements.SingleAsync(movementSearchedInTheDatabaseTable =>
            movementSearchedInTheDatabaseTable.Id == movementBetweenTheTwoBagsOfThisTest.Id);
        Assert.Null(movementKeptInTheHistoryOfTheOtherBag.SourceBagId);
        Assert.Equal(otherBagKeptByThisTest.Id, movementKeptInTheHistoryOfTheOtherBag.DestinationBagId);
    }
}
