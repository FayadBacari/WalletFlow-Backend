namespace WalletFlow.Application.BankConnections;

// L'heure de la dernière mise à jour en arrière-plan de chaque connexion : au plus une toutes les 6 heures,
// soit 4 par jour (GR Comptes). Implémenté dans la couche Infrastructure.
public interface IBankUpdateSchedule
{
    // Note l'heure et renvoie vrai si cette connexion n'a pas été mise à jour en arrière-plan depuis 6 heures. Sinon, renvoie faux.
    bool TryStartBackgroundUpdate(Guid connectionId);
}
