using WalletFlow.Application.Persistence;

namespace WalletFlow.Application.Bags;

// Cas d'utilisation : l'utilisateur connecté supprime un de ses sacs (GR Sacs). Son argent revient dans l'argent disponible :
// la base efface ses mouvements avec l'argent disponible, et ceux avec un autre sac gardent l'autre sac (MPD).
public class BagDeletionService
{
    private readonly IBagRepository bagRepository;
    private readonly DatabaseChangesSavingService savingService;

    // Reçoit l'accès aux sacs et l'enregistrement avec réessai.
    public BagDeletionService(IBagRepository bagRepository, DatabaseChangesSavingService savingService)
    {
        this.bagRepository = bagRepository;
        this.savingService = savingService;
    }

    // Supprime le sac s'il est à l'utilisateur. Deux suppressions simultanées de deux sacs reliés par un mouvement
    // se gênent dans la base : la seconde échoue, puis réussit au passage suivant, quand la première est enregistrée.
    public async Task<BagDeletionResult> DeleteAsync(Guid userId, Guid bagId)
    {
        return await savingService.SaveWithRetriesAsync(
            () => PrepareDeletionAsync(userId, bagId),
            new BagDeletionResult.NotSaved());
    }

    // Un passage : lit le sac parmi ceux de l'utilisateur, puis prépare sa suppression.
    private async Task<PreparedChanges<BagDeletionResult>> PrepareDeletionAsync(Guid userId, Guid bagId)
    {
        var bag = await bagRepository.FindAsync(userId, bagId);

        if (bag == null)
        {
            return PreparedChanges<BagDeletionResult>.NothingToSave(new BagDeletionResult.NotFound());
        }

        bagRepository.PrepareDeletion(bag);

        return PreparedChanges<BagDeletionResult>.SaveThenReturn(new BagDeletionResult.Deleted());
    }
}
