using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Security;

// Écriture dans le journal de sécurité (SS §11). Implémenté dans la couche Infrastructure.
public interface ISecurityEventRepository
{
    // Enregistre tout de suite un événement dans le journal de sécurité.
    Task SaveEventAsync(SecurityEvent securityEvent);

    // Prépare un événement : il partira avec les autres changements, dans la même transaction, à l'appel de l'unité de travail.
    void PrepareEvent(SecurityEvent securityEvent);

    // Efface tout de suite les événements de plus d'un an (SS §11), et renvoie leur nombre. La durée est gardée par la base :
    // l'application n'a pas le droit d'effacer un événement plus récent.
    Task<int> DeleteExpiredEventsAsync();
}
