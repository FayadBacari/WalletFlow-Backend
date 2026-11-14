using System.Net;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Security;

// Note au journal de sécurité les tentatives d'accès à une ressource d'un autre utilisateur (SS §11). La réponse, elle, reste
// celle d'une ressource qui n'existe pas (404, SS §4) : rien ne dit à l'utilisateur que la ressource existe.
// Seul le temps de réponse change un peu, d'une écriture au journal : il faudrait déjà connaître l'identifiant, tiré au hasard
// sur 122 bits, pour s'en servir.
public class ForeignAccessJournalService
{
    private readonly IResourceOwnershipRepository ownershipRepository;
    private readonly SecurityJournalService securityJournal;

    // Reçoit la recherche du propriétaire d'une ressource et le journal de sécurité.
    public ForeignAccessJournalService(IResourceOwnershipRepository ownershipRepository, SecurityJournalService securityJournal)
    {
        this.ownershipRepository = ownershipRepository;
        this.securityJournal = securityJournal;
    }

    // Note un accès refusé si cette ressource existe et appartient à un autre utilisateur. Une ressource inconnue, ou qui est
    // à l'utilisateur lui-même, ne laisse rien : elle vient d'être supprimée, ou c'est une faute de frappe.
    // Un identifiant absent ne vise rien. Écriture immédiate : l'appelant n'a plus rien à enregistrer.
    public async Task RecordIfForeignAsync(Guid userId, IPAddress? ipAddress, OwnedResource resource, Guid? resourceId)
    {
        if (resourceId is not Guid id)
        {
            return;
        }

        var ownerId = await ownershipRepository.FindOwnerAsync(resource, id);

        if (ownerId == null || ownerId == userId)
        {
            return;
        }

        await securityJournal.SaveEventNowAsync(SecurityEventType.AccessDenied, SecurityEventResult.Failure, userId, ipAddress);
    }
}
