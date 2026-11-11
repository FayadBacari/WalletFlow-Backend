using System.Net;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Security;

// Écrit les événements du journal de sécurité (SS §11). Chaque événement est créé ici, toujours de la même façon :
// date, utilisateur, adresse IP, événement et résultat. Jamais l'adresse e-mail (SS §7).
public class SecurityJournalService
{
    private readonly ISecurityEventRepository securityEventRepository;
    private readonly TimeProvider clock;

    // Reçoit le dépôt du journal et l'horloge.
    public SecurityJournalService(ISecurityEventRepository securityEventRepository, TimeProvider clock)
    {
        this.securityEventRepository = securityEventRepository;
        this.clock = clock;
    }

    // Écrit tout de suite un événement dans le journal : pour un refus, quand rien d'autre n'est à enregistrer.
    public async Task SaveEventNowAsync(
        SecurityEventType type,
        SecurityEventResult result,
        Guid? accountId,
        IPAddress? ipAddress)
    {
        await securityEventRepository.SaveEventAsync(
            CreateEvent(type, result, accountId, ipAddress));
    }

    // Prépare un événement qui partira avec les autres changements, dans la même transaction :
    // le journal ne dit donc jamais qu'une action a réussi si elle n'a pas été enregistrée.
    public void PrepareEvent(
        SecurityEventType type,
        SecurityEventResult result,
        Guid? accountId,
        IPAddress? ipAddress)
    {
        securityEventRepository.PrepareEvent(
            CreateEvent(type, result, accountId, ipAddress));
    }

    // Crée l'événement, daté par l'horloge de l'application.
    private SecurityEvent CreateEvent(
        SecurityEventType type,
        SecurityEventResult result,
        Guid? accountId,
        IPAddress? ipAddress)
    {
        return new SecurityEvent
        {
            Type = type,
            Result = result,
            OccurredAt = clock.GetUtcNow().UtcDateTime,
            IpAddress = ipAddress,
            UserId = accountId
        };
    }
}
