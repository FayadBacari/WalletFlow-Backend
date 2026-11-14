using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Security;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.BankTestRows;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie dans la vraie base que le propriétaire de chaque sorte de ressource est retrouvé (SS §11).
// La transaction est annulée à la fin : la base reste vide.
public class ResourceOwnershipRepositoryTests
{
    [Fact]
    public async Task OwnerOfEachKindOfResourceIsFound()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveAccountAsync(databaseContext, "test-proprietaires@exemple.test");
        var otherUser = await SaveAccountAsync(databaseContext, "test-proprietaires-autre@exemple.test");
        var (connection, account) = await SaveConnectionAsync(databaseContext, user.Id, "stable-proprietaire");
        var category = await databaseContext.Categories.SingleAsync(row => row.UserId == user.Id && row.Name == "Alimentation");
        var operation = CreateTransaction(account.Id, category.Id, "operation-proprietaire");
        await SaveTransactionsAsync(databaseContext, operation);
        var bag = new Bag { Name = "Vacances", UserId = user.Id };
        var otherBag = new Bag { Name = "Sac d'un autre", UserId = otherUser.Id };
        databaseContext.Bags.AddRange(bag, otherBag);
        await databaseContext.SaveChangesAsync();
        var repository = new ResourceOwnershipRepository(databaseContext);

        Assert.Equal(user.Id, await repository.FindOwnerAsync(OwnedResource.Category, category.Id));
        Assert.Equal(user.Id, await repository.FindOwnerAsync(OwnedResource.BankConnection, connection.Id));
        Assert.Equal(user.Id, await repository.FindOwnerAsync(OwnedResource.BankAccount, account.Id));
        Assert.Equal(user.Id, await repository.FindOwnerAsync(OwnedResource.Transaction, operation.Id));
        Assert.Equal(user.Id, await repository.FindOwnerAsync(OwnedResource.Bag, bag.Id));
        Assert.Equal(otherUser.Id, await repository.FindOwnerAsync(OwnedResource.Bag, otherBag.Id));
    }

    // Un identifiant inconnu, ou celui d'une ressource d'une autre sorte, n'a pas de propriétaire.
    [Fact]
    public async Task UnknownOrWrongKindOfResourceHasNoOwner()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveAccountAsync(databaseContext, "test-proprietaires-inconnus@exemple.test");
        var (connection, account) = await SaveConnectionAsync(databaseContext, user.Id, "stable-inconnu");
        var repository = new ResourceOwnershipRepository(databaseContext);

        foreach (var resource in Enum.GetValues<OwnedResource>())
        {
            Assert.Null(await repository.FindOwnerAsync(resource, Guid.NewGuid()));
        }

        Assert.Null(await repository.FindOwnerAsync(OwnedResource.Bag, account.Id));
        Assert.Null(await repository.FindOwnerAsync(OwnedResource.Transaction, connection.Id));
    }
}
