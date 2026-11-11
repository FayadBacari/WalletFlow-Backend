using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la suppression d'un sac dans la vraie base (GR Sacs, MPD §7). La transaction est annulée à la fin.
public class BagDeletionTests
{
    // Les mouvements restent chargés en mémoire pendant la suppression : EF Core ne doit pas y toucher,
    // c'est la base qui applique ses règles (déclencheur et ON DELETE SET NULL).
    [Fact]
    public async Task BagWithItsHistoryLoadedIsDeletedAndTheDatabaseRulesApply()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();

        var user = new User { Email = "test-sac-suppression@exemple.test", PasswordHash = "empreinte-de-test" };
        var deletedBag = new Bag { Name = "Sac supprimé", UserId = user.Id };
        var otherBag = new Bag { Name = "Sac conservé", UserId = user.Id };
        var movementDate = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        var incomingMovement = new BagMovement { AmountInCents = 1000, MovedAt = movementDate, DestinationBagId = deletedBag.Id };
        var transferMovement = new BagMovement { AmountInCents = 500, MovedAt = movementDate, SourceBagId = deletedBag.Id, DestinationBagId = otherBag.Id };
        databaseContext.Users.Add(user);
        databaseContext.Bags.AddRange(deletedBag, otherBag);
        databaseContext.BagMovements.AddRange(incomingMovement, transferMovement);
        await databaseContext.SaveChangesAsync();

        databaseContext.Bags.Remove(deletedBag);
        await databaseContext.SaveChangesAsync();

        // On oublie les objets en mémoire : tout ce qui suit est vraiment relu dans la base.
        databaseContext.ChangeTracker.Clear();

        Assert.False(await databaseContext.Bags.AnyAsync(row => row.Id == deletedBag.Id));

        // Le mouvement avec l'argent disponible disparaît, celui avec l'autre sac garde l'autre sac.
        Assert.False(await databaseContext.BagMovements.AnyAsync(row => row.Id == incomingMovement.Id));
        var keptMovement = await databaseContext.BagMovements.SingleAsync(row =>
            row.Id == transferMovement.Id);
        Assert.Null(keptMovement.SourceBagId);
        Assert.Equal(otherBag.Id, keptMovement.DestinationBagId);
    }
}
