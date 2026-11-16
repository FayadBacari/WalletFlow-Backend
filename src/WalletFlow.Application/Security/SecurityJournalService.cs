using System.Net;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Security;

// Écrit les événements du journal de sécurité (SS §11). Chaque événement est créé ici, toujours de la même façon :
// date, utilisateur, adresse IP, événement et résultat, et pour une action de l'administrateur, le compte qu'elle vise.
// Jamais l'adresse e-mail (SS §7).
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

    // Écrit tout de suite l'action d'un administrateur, avec le compte qu'elle vise s'il y en a un (SS §8, §11) :
    // pour une recherche, quand rien d'autre n'est à enregistrer.
    public async Task SaveAdminActionNowAsync(Guid administratorId, Guid? targetAccountId, IPAddress? ipAddress)
    {
        await securityEventRepository.SaveEventAsync(
            CreateEvent(SecurityEventType.AdminAction, SecurityEventResult.Success, administratorId, ipAddress, targetAccountId));
    }

    // Prépare l'action d'un administrateur, avec le compte qu'elle vise s'il y en a un : elle partira avec ce qu'elle change.
    public void PrepareAdminAction(Guid administratorId, Guid? targetAccountId, IPAddress? ipAddress)
    {
        securityEventRepository.PrepareEvent(
            CreateEvent(SecurityEventType.AdminAction, SecurityEventResult.Success, administratorId, ipAddress, targetAccountId));
    }

    // Crée l'événement, daté par l'horloge de l'application. Seule une action de l'administrateur vise un compte.
    private SecurityEvent CreateEvent(
        SecurityEventType type,
        SecurityEventResult result,
        Guid? accountId,
        IPAddress? ipAddress,
        Guid? targetAccountId = null)
    {
        return new SecurityEvent
        {
            Type = type,
            Result = result,
            OccurredAt = clock.GetUtcNow().UtcDateTime,
            IpAddress = ipAddress,
            UserId = accountId,
            TargetUserId = targetAccountId
        };
    }
}
