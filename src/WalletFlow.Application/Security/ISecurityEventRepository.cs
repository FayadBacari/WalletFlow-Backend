using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Security;

// Écriture dans le journal de sécurité (SS §11). Implémenté dans la couche Infrastructure.
public interface ISecurityEventRepository
{
    // Enregistre tout de suite un événement dans le journal de sécurité.
    Task SaveEventAsync(SecurityEvent securityEvent);

    // Prépare un événement : il partira avec les autres changements, dans la même transaction, à l'appel de l'unité de travail.
    void PrepareEvent(SecurityEvent securityEvent);
}
