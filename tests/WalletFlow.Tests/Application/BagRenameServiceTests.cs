using WalletFlow.Application.Bags;

namespace WalletFlow.Tests.Application;

// Vérifie le changement de nom d'un sac (GR Sacs, SS §4 et §5).
public class BagRenameServiceTests
{
    [Fact]
    public async Task BagGetsTheCleanedNameAndKeepsItsBalance()
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");
        services.SaveMovement(null, holidays, 12_000);

        var result = await services.CreateRename().RenameAsync(services.UserId, holidays.Id, "  Voyage ");

        var renamed = Assert.IsType<BagRenameResult.Renamed>(result);
        Assert.Equal("Voyage", holidays.Name);
        Assert.Equal("Voyage", renamed.Bag.Name);
        Assert.Equal(12_000, renamed.Bag.BalanceInCents);
        Assert.Equal(1, services.UnitOfWork.CommittedTransactionCount);
    }

    [Fact]
    public async Task NameOfAnotherBagCanBeTakenAgain()
    {
        var services = new BagServices();
        services.SaveBag("Voyage");
        var holidays = services.SaveBag("Vacances");

        var result = await services.CreateRename().RenameAsync(services.UserId, holidays.Id, "Voyage");

        Assert.IsType<BagRenameResult.Renamed>(result);
        Assert.Equal(2, services.Repository.SavedBags.Count(bag => bag.Name == "Voyage"));
    }

    [Fact]
    public async Task NameThatBreaksTheRulesChangesNothing()
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");
        var rename = services.CreateRename();

        Assert.IsType<BagRenameResult.NameBreaksRules>(await rename.RenameAsync(services.UserId, holidays.Id, " "));
        Assert.IsType<BagRenameResult.NameBreaksRules>(await rename.RenameAsync(services.UserId, holidays.Id, new string('a', 51)));
        Assert.Equal("Vacances", holidays.Name);
    }

    // SS §4 : le sac d'un autre utilisateur n'est pas trouvé, comme un sac qui n'existe pas.
    [Fact]
    public async Task BagOfAnotherUserIsNotFound()
    {
        var services = new BagServices();
        var otherUserBag = services.SaveBag("Sac d'un autre", Guid.NewGuid());
        var rename = services.CreateRename();

        Assert.IsType<BagRenameResult.NotFound>(await rename.RenameAsync(services.UserId, otherUserBag.Id, "Voyage"));
        Assert.IsType<BagRenameResult.NotFound>(await rename.RenameAsync(services.UserId, Guid.NewGuid(), "Voyage"));
        Assert.Equal("Sac d'un autre", otherUserBag.Name);
    }

    // Une autre requête supprime le sac juste avant cet enregistrement : le passage suivant ne le trouve plus.
    [Fact]
    public async Task BagDeletedAtTheSameMomentIsNotFoundAtTheNextTry()
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");
        services.Repository.BagDeletedBeforeSave = holidays.Id;

        var result = await services.CreateRename().RenameAsync(services.UserId, holidays.Id, "Voyage");

        Assert.IsType<BagRenameResult.NotFound>(result);
        Assert.Empty(services.Repository.SavedBags);
    }

    // Après 10 conflits, rien n'est gardé : le sac reprend le nom qu'il avait à sa lecture.
    [Fact]
    public async Task NameIsKeptAfterTenConflicts()
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");
        services.Repository.Conflicts = 10;

        var result = await services.CreateRename().RenameAsync(services.UserId, holidays.Id, "Voyage");

        Assert.IsType<BagRenameResult.NotSaved>(result);
        Assert.Equal("Vacances", holidays.Name);
    }
}
