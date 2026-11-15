using WalletFlow.Application.Bags;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Application.Notifications;

// L'alerte d'un sac qui atteint son objectif (GR Sacs), une seule fois par objectif : un nouvel objectif peut en déclencher
// une autre. Elle se prépare dans le passage de l'appelant, qui a lu la ligne de l'utilisateur avant tout et la réécrit :
// les déplacements et les changements d'objectif d'un même utilisateur passent ainsi un par un, et le solde comme l'objectif
// lus ici sont ceux que l'enregistrement garde.
public class BagGoalAlerts
{
    private readonly IBagRepository bagRepository;
    private readonly IAlertRepository alertRepository;
    private readonly AlertPreparation alertPreparation;

    // Reçoit l'accès aux sacs et aux alertes, et la préparation des alertes.
    public BagGoalAlerts(IBagRepository bagRepository, IAlertRepository alertRepository, AlertPreparation alertPreparation)
    {
        this.bagRepository = bagRepository;
        this.alertRepository = alertRepository;
        this.alertPreparation = alertPreparation;
    }

    // Ce montant arrive dans ce sac de l'utilisateur, avec le mouvement que l'appelant prépare : si le sac atteint alors
    // son objectif pour la première fois, son alerte part avec le mouvement.
    public async Task PrepareAfterArrivalAsync(Guid userId, Guid bagId, int arrivingAmountInCents, DateTime now)
    {
        var bag = await bagRepository.FindAsync(userId, bagId);

        if (bag == null)
        {
            return;
        }

        var balances = await bagRepository.SumBalancesAsync(userId);
        var balanceAfterInCents = balances.GetValueOrDefault(bagId) + arrivingAmountInCents;

        if (!AlertRules.IsBagGoalReached(balanceAfterInCents, bag.TargetAmountInCents)
            || await alertRepository.FindBagAlertAsync(bagId) != null)
        {
            return;
        }

        await alertPreparation.TryPrepareAsync(Alert.ForBagGoal(userId, bagId, now));
    }

    // L'objectif de ce sac vient de changer. Un nouveau montant, ou l'objectif enlevé, efface l'alerte de l'ancien (DD §9.3) ;
    // changer seulement la date garde le même montant à atteindre, et donc son alerte. Puis l'alerte part si le sac
    // atteint déjà le nouveau montant.
    public async Task PrepareAfterGoalChangeAsync(Bag bag, int? previousTargetInCents, long balanceInCents, DateTime now)
    {
        var alert = await alertRepository.FindBagAlertAsync(bag.Id);

        if (alert != null && previousTargetInCents != bag.TargetAmountInCents)
        {
            alertRepository.PrepareDeletion(alert);
            alert = null;
        }

        if (alert == null && AlertRules.IsBagGoalReached(balanceInCents, bag.TargetAmountInCents))
        {
            await alertPreparation.TryPrepareAsync(Alert.ForBagGoal(bag.UserId, bag.Id, now));
        }
    }
}
