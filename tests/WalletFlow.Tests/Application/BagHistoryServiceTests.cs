using WalletFlow.Application.Bags;

namespace WalletFlow.Tests.Application;

// Vérifie l'historique des mouvements d'un sac (GR Sacs, SS §4).
public class BagHistoryServiceTests
{
    // Le montant est positif quand l'argent arrive, négatif quand il part ; l'autre côté est un sac ou l'argent disponible.
    [Fact]
    public async Task HistoryGivesTheNewestMovementsFirstSeenFromTheBag()
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");
        var savings = services.SaveBag("Épargne");
        var arrival = services.SaveMovement(null, holidays, 50_000, services.Now.AddHours(-3));
        var transfer = services.SaveMovement(holidays, savings, 20_000, services.Now.AddHours(-2));
        var withdrawal = services.SaveMovement(holidays, null, 5000, services.Now.AddHours(-1));
        services.SaveMovement(null, savings, 1000, services.Now);

        var result = await services.CreateHistory().ReadAsync(services.UserId, holidays.Id, 1);

        var page = Assert.IsType<BagHistoryResult.Found>(result).Page;
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(new List<Guid> { withdrawal.Id, transfer.Id, arrival.Id }, page.Items.Select(item => item.Id).ToList());
        Assert.Equal(new List<int> { -5000, -20_000, 50_000 }, page.Items.Select(item => item.AmountInCents).ToList());
        Assert.Null(page.Items[0].OtherBagId);
        Assert.Equal(savings.Id, page.Items[1].OtherBagId);
        Assert.Equal("Épargne", page.Items[1].OtherBagName);
        Assert.Null(page.Items[2].OtherBagName);
        Assert.Equal(services.Now.AddHours(-3), page.Items[2].MovedAt);
    }

    // 50 mouvements par page : le cinquante et unième est seul sur la deuxième page.
    [Fact]
    public async Task HistoryIsReadFiftyMovementsAtATime()
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");

        for (var minute = 0; minute <= 50; minute++)
        {
            services.SaveMovement(null, holidays, 100, services.Now.AddMinutes(-minute));
        }

        var firstPage = Assert.IsType<BagHistoryResult.Found>(await services.CreateHistory().ReadAsync(services.UserId, holidays.Id, 1)).Page;
        var secondPage = Assert.IsType<BagHistoryResult.Found>(await services.CreateHistory().ReadAsync(services.UserId, holidays.Id, 2)).Page;

        Assert.Equal(50, BagHistoryService.PageSize);
        Assert.Equal(50, firstPage.Items.Count);
        Assert.Single(secondPage.Items);
        Assert.Equal(services.Now.AddMinutes(-50), secondPage.Items[0].MovedAt);
        Assert.Equal(51, secondPage.TotalCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    public async Task PageOutsideTheLimitsIsRefused(int page)
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");

        var result = await services.CreateHistory().ReadAsync(services.UserId, holidays.Id, page);

        Assert.IsType<BagHistoryResult.PageBreaksRules>(result);
    }

    [Fact]
    public async Task LastPageAllowedIsReadEvenWhenEmpty()
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");

        var result = await services.CreateHistory().ReadAsync(services.UserId, holidays.Id, BagHistoryService.MaximumPage);

        Assert.Equal(10_000, BagHistoryService.MaximumPage);
        Assert.Empty(Assert.IsType<BagHistoryResult.Found>(result).Page.Items);
    }

    // SS §4 : l'historique du sac d'un autre utilisateur n'est pas trouvé, comme un sac qui n'existe pas.
    [Fact]
    public async Task HistoryOfAnotherUserBagIsNotFound()
    {
        var services = new BagServices();
        var otherUserBag = services.SaveBag("Sac d'un autre", Guid.NewGuid());
        services.SaveMovement(null, otherUserBag, 5000);
        var history = services.CreateHistory();

        Assert.IsType<BagHistoryResult.NotFound>(await history.ReadAsync(services.UserId, otherUserBag.Id, 1));
        Assert.IsType<BagHistoryResult.NotFound>(await history.ReadAsync(services.UserId, Guid.NewGuid(), 1));
    }

    // Quand l'autre sac est supprimé, son mouvement compte comme un mouvement avec l'argent disponible (MPD).
    [Fact]
    public async Task MovementWithADeletedBagShowsTheAvailableMoney()
    {
        var services = new BagServices();
        var holidays = services.SaveBag("Vacances");
        var savings = services.SaveBag("Épargne");
        services.SaveMovement(null, holidays, 50_000);
        services.SaveMovement(holidays, savings, 20_000);
        await services.CreateDeletion().DeleteAsync(services.UserId, holidays.Id);

        var page = Assert.IsType<BagHistoryResult.Found>(await services.CreateHistory().ReadAsync(services.UserId, savings.Id, 1)).Page;

        var item = Assert.Single(page.Items);
        Assert.Equal(20_000, item.AmountInCents);
        Assert.Null(item.OtherBagId);
        Assert.Null(item.OtherBagName);
    }
}
