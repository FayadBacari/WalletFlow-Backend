using WalletFlow.Application.Bags;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Tests.Application;

// Vérifie l'alerte d'un sac qui atteint son objectif (GR Sacs, DD §9.3) : une seule fois par objectif, partie avec
// le déplacement qui l'atteint, et effacée quand le montant à atteindre change.
public class BagGoalAlertsTests
{
    private readonly BagServices services = new BagServices();
    private readonly Bag bag;

    // Donne à l'utilisateur 1 000,00 € à la banque, et un sac « Vacances » avec un objectif de 100,00 €.
    public BagGoalAlertsTests()
    {
        services.AddBankAccount(100_000);
        bag = services.SaveBag("Vacances");
        bag.ReplaceGoal(10_000, null, services.Today);
    }

    private async Task<BagMovementResult> MoveAsync(Guid? sourceBagId, Guid? destinationBagId, int amountInCents)
    {
        return await services.CreateMovement().MoveAsync(services.UserId, sourceBagId, destinationBagId, amountInCents);
    }

    private async Task<BagGoalResult> ReplaceGoalAsync(int? targetInCents, DateOnly? targetDate = null)
    {
        return await services.CreateGoal().ReplaceAsync(services.UserId, bag.Id, targetInCents, targetDate);
    }

    // Les e-mails d'alerte mis dans la file.
    private List<EmailRequest> QueuedAlertEmails()
    {
        return services.EmailRequests.SavedRequests.Where(request => request.Type == EmailRequestType.Alert).ToList();
    }

    [Fact]
    public async Task MoneyThatReachesTheGoalSendsTheAlertWithTheMovement()
    {
        await MoveAsync(null, bag.Id, 4_000);
        Assert.Empty(services.Alerts.SavedAlerts);

        var result = await MoveAsync(null, bag.Id, 6_000);

        var alert = Assert.Single(services.Alerts.SavedAlerts);
        var email = Assert.Single(QueuedAlertEmails());
        Assert.IsType<BagMovementResult.Moved>(result);
        Assert.Equal(AlertKind.BagGoalReached, alert.Kind);
        Assert.Equal(bag.Id, alert.BagId);
        Assert.Equal(services.UserId, alert.UserId);
        Assert.Equal(alert.Id, email.AlertId);
        Assert.Equal(2, services.UnitOfWork.CommittedTransactionCount);
    }

    // GR Sacs : une seule alerte par objectif, même si le sac redescend sous l'objectif puis l'atteint à nouveau.
    [Fact]
    public async Task GoalAlertLeavesOnlyOncePerGoal()
    {
        await MoveAsync(null, bag.Id, 10_000);
        await MoveAsync(bag.Id, null, 5_000);

        await MoveAsync(null, bag.Id, 5_000);

        Assert.Single(services.Alerts.SavedAlerts);
        Assert.Single(QueuedAlertEmails());
    }

    // Seul le sac qui reçoit l'argent peut atteindre son objectif : celui qui le donne, jamais.
    [Fact]
    public async Task MovementBetweenBagsAlertsOnlyTheBagThatReceives()
    {
        var otherBag = services.SaveBag("Voiture");
        otherBag.ReplaceGoal(1, null, services.Today);
        services.SaveMovement(null, otherBag, 20_000);

        await MoveAsync(otherBag.Id, bag.Id, 10_000);

        Assert.Equal(bag.Id, Assert.Single(services.Alerts.SavedAlerts).BagId);
    }

    [Fact]
    public async Task BagWithoutGoalSendsNothing()
    {
        var otherBag = services.SaveBag("Imprévus");

        await MoveAsync(null, otherBag.Id, 50_000);

        Assert.Empty(services.Alerts.SavedAlerts);
        Assert.Empty(QueuedAlertEmails());
    }

    // Un nouveau montant efface l'alerte de l'ancien. Si le sac atteint déjà le nouveau, l'alerte repart tout de suite.
    [Fact]
    public async Task NewAmountAlreadyReachedSendsANewAlert()
    {
        await MoveAsync(null, bag.Id, 10_000);
        var firstAlert = Assert.Single(services.Alerts.SavedAlerts);

        var result = await ReplaceGoalAsync(8_000);

        var newAlert = Assert.Single(services.Alerts.SavedAlerts);
        Assert.IsType<BagGoalResult.Changed>(result);
        Assert.NotEqual(firstAlert.Id, newAlert.Id);
        Assert.Equal(2, QueuedAlertEmails().Count);
        Assert.Equal(newAlert.Id, QueuedAlertEmails().Last().AlertId);
    }

    // Un nouveau montant que le sac n'atteint pas encore efface l'alerte, et attend le déplacement qui l'atteindra.
    [Fact]
    public async Task NewAmountAboveTheBalanceWaitsForTheMovementThatReachesIt()
    {
        await MoveAsync(null, bag.Id, 10_000);

        await ReplaceGoalAsync(20_000);
        Assert.Empty(services.Alerts.SavedAlerts);

        await MoveAsync(null, bag.Id, 10_000);

        Assert.Single(services.Alerts.SavedAlerts);
        Assert.Equal(2, QueuedAlertEmails().Count);
    }

    // Changer seulement la date garde le même montant à atteindre : l'alerte reste, et aucun e-mail ne repart.
    [Fact]
    public async Task ChangingOnlyTheDateKeepsTheAlert()
    {
        await MoveAsync(null, bag.Id, 10_000);
        var alert = Assert.Single(services.Alerts.SavedAlerts);

        await ReplaceGoalAsync(10_000, services.Today.AddDays(30));

        Assert.Equal(alert, Assert.Single(services.Alerts.SavedAlerts));
        Assert.Single(QueuedAlertEmails());
    }

    [Fact]
    public async Task RemovingTheGoalErasesItsAlert()
    {
        await MoveAsync(null, bag.Id, 10_000);

        await ReplaceGoalAsync(null);

        Assert.Empty(services.Alerts.SavedAlerts);
    }

    // DD §9.3 : une alerte que l'utilisateur a désactivée n'est pas créée.
    [Fact]
    public async Task DisabledBagAlertIsNotCreated()
    {
        var bagType = services.Alerts.SavedTypes.Single(type => type.Kind == AlertKind.BagGoalReached);
        services.Alerts.SavedPreferences.Add(AlertPreference.Create(services.UserId, bagType, false));

        await MoveAsync(null, bag.Id, 10_000);
        await ReplaceGoalAsync(5_000);

        Assert.Empty(services.Alerts.SavedAlerts);
        Assert.Empty(QueuedAlertEmails());
    }

    // Un changement d'objectif réécrit la ligne de l'utilisateur, comme un déplacement : les deux passent un par un.
    [Fact]
    public async Task GoalChangeRewritesTheUserRow()
    {
        await ReplaceGoalAsync(5_000);

        Assert.Equal(services.User, Assert.Single(services.Repository.CommittedUserChecks));
    }

    // Un déplacement enregistré pendant le changement d'objectif le fait recommencer : l'alerte se décide sur le solde à jour.
    [Fact]
    public async Task MovementSavedDuringAGoalChangeIsSeenAtTheNextTry()
    {
        services.Repository.MovementSavedBeforeSave = new BagMovement
        {
            AmountInCents = 10_000,
            MovedAt = services.Now,
            DestinationBagId = bag.Id
        };

        var result = await ReplaceGoalAsync(9_000);

        Assert.IsType<BagGoalResult.Changed>(result);
        Assert.Equal(bag.Id, Assert.Single(services.Alerts.SavedAlerts).BagId);
        Assert.Single(QueuedAlertEmails());
    }

    // Le mouvement et l'alerte partent ensemble, ou rien : une panne de la file d'e-mails ne garde aucun des deux.
    [Fact]
    public async Task MovementIsNotKeptWhenItsAlertCannotBeQueued()
    {
        services.EmailRequests.IsDown = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => MoveAsync(null, bag.Id, 10_000));

        Assert.Empty(services.Repository.SavedMovements);
        Assert.Empty(services.Alerts.SavedAlerts);
    }
}
