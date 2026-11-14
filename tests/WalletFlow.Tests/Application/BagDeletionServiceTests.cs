using WalletFlow.Application.Bags;

namespace WalletFlow.Tests.Application;

// Vérifie la suppression d'un sac (GR Sacs, SS §4). La fausse base suit les règles de la vraie, testées dans BagRepositoryTests.
public class BagDeletionServiceTests
{
    // GR Sacs : supprimer un sac remet son montant dans l'argent disponible. L'autre sac garde son solde.
    [Fact]
    public async Task DeletedBagGivesItsMoneyBackToTheAvailableMoney()
    {
        var services = new BagServices();
        services.AddBankAccount(100_000);
        var holidays = services.SaveBag("Vacances");
        var savings = services.SaveBag("Épargne");
        services.SaveMovement(null, holidays, 50_000);
        services.SaveMovement(holidays, savings, 20_000);

        var result = await services.CreateDeletion().DeleteAsync(services.UserId, holidays.Id);
        var summary = await services.CreateList().ListAsync(services.UserId);

        Assert.IsType<BagDeletionResult.Deleted>(result);
        Assert.DoesNotContain(holidays, services.Repository.SavedBags);
        Assert.Equal(20_000, services.BalanceOf(savings));
        Assert.Equal(80_000, summary.AvailableInCents);
        var keptMovement = Assert.Single(services.Repository.SavedMovements);
        Assert.Null(keptMovement.SourceBagId);
        Assert.Equal(savings.Id, keptMovement.DestinationBagId);
    }

    // SS §4 : le sac d'un autre utilisateur n'est pas trouvé, comme un sac qui n'existe pas, et rien n'est supprimé.
    [Fact]
    public async Task BagOfAnotherUserIsNotFound()
    {
        var services = new BagServices();
        var otherUserBag = services.SaveBag("Sac d'un autre", Guid.NewGuid());
        services.SaveMovement(null, otherUserBag, 5000);
        var deletion = services.CreateDeletion();

        Assert.IsType<BagDeletionResult.NotFound>(await deletion.DeleteAsync(services.UserId, otherUserBag.Id));
        Assert.IsType<BagDeletionResult.NotFound>(await deletion.DeleteAsync(services.UserId, Guid.NewGuid()));
        Assert.Contains(otherUserBag, services.Repository.SavedBags);
        Assert.Single(services.Repository.SavedMovements);
    }

    // Une autre requête supprime le même sac juste avant cet enregistrement : le passage suivant ne le trouve plus.
    [Fact]
    public async Task BagDeletedAtTheSameMomentIsNotFoundAtTheNextTry()
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");
        services.Repository.BagDeletedBeforeSave = holidays.Id;

        var result = await services.CreateDeletion().DeleteAsync(services.UserId, holidays.Id);

        Assert.IsType<BagDeletionResult.NotFound>(result);
        Assert.Empty(services.Repository.SavedBags);
    }

    // Après 10 conflits, rien n'est supprimé.
    [Fact]
    public async Task BagStaysAfterTenConflicts()
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");
        services.SaveMovement(null, holidays, 5000);
        services.Repository.Conflicts = 10;

        var result = await services.CreateDeletion().DeleteAsync(services.UserId, holidays.Id);

        Assert.IsType<BagDeletionResult.NotSaved>(result);
        Assert.Contains(holidays, services.Repository.SavedBags);
        Assert.Equal(5000, services.BalanceOf(holidays));
    }
}
