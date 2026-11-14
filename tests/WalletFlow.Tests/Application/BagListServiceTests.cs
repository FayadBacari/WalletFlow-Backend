using WalletFlow.Application.Bags;

namespace WalletFlow.Tests.Application;

// Vérifie la liste des sacs, avec leur solde, leur progression et l'argent disponible (GR Sacs, DD §11).
public class BagListServiceTests
{
    // L'ordre alphabétique français : « épargne » se range avec les E, les majuscules ne comptent pas.
    [Fact]
    public async Task BagsAreInFrenchAlphabeticalOrder()
    {
        var services = new BagServices();
        services.SaveBag("Voyage");
        services.SaveBag((char)0x00E9 + "pargne");
        services.SaveBag("Zèbre");
        services.SaveBag("auto");

        var summary = await services.CreateList().ListAsync(services.UserId);

        Assert.Equal(
            new List<string> { "auto", (char)0x00E9 + "pargne", "Voyage", "Zèbre" },
            summary.Bags.Select(bag => bag.Name).ToList());
    }

    // Deux sacs du même nom gardent le même ordre d'un affichage à l'autre : celui de leur identifiant.
    [Fact]
    public async Task BagsWithTheSameNameAreOrderedByTheirId()
    {
        var services = new BagServices();
        var first = services.SaveBag("Vacances");
        var second = services.SaveBag("Vacances");
        var expectedIds = new List<Guid> { first.Id, second.Id };
        expectedIds.Sort();
        services.Repository.SavedBags.Reverse();

        var summary = await services.CreateList().ListAsync(services.UserId);

        Assert.Equal(expectedIds, summary.Bags.Select(bag => bag.Id).ToList());
    }

    // DD §11 : le solde d'un sac est ce qui est arrivé moins ce qui est parti, la progression ce solde sur l'objectif.
    [Fact]
    public async Task EachBagHasItsBalanceAndItsProgress()
    {
        var services = new BagServices();
        services.AddBankAccount(200_000);
        var holidays = services.SaveBag("Vacances");
        holidays.ReplaceGoal(80_000, null, services.Today);
        var savings = services.SaveBag("Épargne");
        services.SaveMovement(null, holidays, 50_000);
        services.SaveMovement(holidays, savings, 10_000);

        var summary = await services.CreateList().ListAsync(services.UserId);

        var holidaysView = summary.Bags.Single(bag => bag.Id == holidays.Id);
        var savingsView = summary.Bags.Single(bag => bag.Id == savings.Id);
        Assert.Equal(40_000, holidaysView.BalanceInCents);
        Assert.Equal(80_000, holidaysView.TargetAmountInCents);
        Assert.Equal(50, holidaysView.ProgressPercent);
        Assert.Equal(10_000, savingsView.BalanceInCents);
        Assert.Null(savingsView.ProgressPercent);
    }

    // GR Sacs : l'argent qui n'est dans aucun sac est l'argent disponible : le solde total moins le total des sacs.
    [Fact]
    public async Task AvailableMoneyIsTheTotalBalanceMinusTheBags()
    {
        var services = new BagServices();
        services.AddBankAccount(120_000);
        var olderAccount = services.AddBankAccount(30_000);
        olderAccount.UpdateBalance(30_000, services.Now.AddHours(-5));
        var holidays = services.SaveBag("Vacances");
        var savings = services.SaveBag("Épargne");
        services.SaveMovement(null, holidays, 50_000);
        services.SaveMovement(null, savings, 25_000);

        var summary = await services.CreateList().ListAsync(services.UserId);

        Assert.Equal(150_000, summary.TotalBalanceInCents);
        Assert.Equal(75_000, summary.BagsTotalInCents);
        Assert.Equal(75_000, summary.AvailableInCents);
        Assert.Equal(services.Now.AddHours(-5), summary.OldestBalanceUpdatedAt);
    }

    // GR Sacs : quand le solde passe sous le total des sacs, l'argent disponible devient négatif : c'est le montant à retirer.
    [Fact]
    public async Task AvailableMoneyIsNegativeWhenTheBagsGoAboveTheTotalBalance()
    {
        var services = new BagServices();
        var account = services.AddBankAccount(100_000);
        var holidays = services.SaveBag("Vacances");
        services.SaveMovement(null, holidays, 100_000);
        account.UpdateBalance(70_000, services.Now);

        var summary = await services.CreateList().ListAsync(services.UserId);

        Assert.Equal(-30_000, summary.AvailableInCents);
    }

    // SS §4 : ni les sacs ni les comptes d'un autre utilisateur n'entrent dans la liste.
    [Fact]
    public async Task BagsAndAccountsOfAnotherUserAreLeftOut()
    {
        var services = new BagServices();
        var otherUserId = Guid.NewGuid();
        services.AddBankAccount(500_000, otherUserId);
        var otherUserBag = services.SaveBag("Sac d'un autre", otherUserId);
        services.SaveMovement(null, otherUserBag, 200_000);

        var summary = await services.CreateList().ListAsync(services.UserId);

        Assert.Empty(summary.Bags);
        Assert.Equal(0, summary.TotalBalanceInCents);
        Assert.Equal(0, summary.BagsTotalInCents);
        Assert.Equal(0, summary.AvailableInCents);
        Assert.Null(summary.OldestBalanceUpdatedAt);
    }
}
