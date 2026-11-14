using WalletFlow.Application.Persistence;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Bags;

// Cas d'utilisation : l'utilisateur connecté renomme un de ses sacs (GR Sacs). Deux sacs peuvent porter le même nom.
public class BagRenameService
{
    private readonly IBagRepository bagRepository;
    private readonly DatabaseChangesSavingService savingService;

    // Reçoit l'accès aux sacs et l'enregistrement avec réessai.
    public BagRenameService(IBagRepository bagRepository, DatabaseChangesSavingService savingService)
    {
        this.bagRepository = bagRepository;
        this.savingService = savingService;
    }

    // Donne le nom tapé au sac, s'il respecte les règles.
    public async Task<BagRenameResult> RenameAsync(Guid userId, Guid bagId, string? typedName)
    {
        var name = BagRules.NormalizeName(typedName);

        if (!BagRules.IsNameValid(name))
        {
            return new BagRenameResult.NameBreaksRules();
        }

        return await savingService.SaveWithRetriesAsync(
            () => PrepareNewNameAsync(userId, bagId, name),
            new BagRenameResult.NotSaved());
    }

    // Un passage : lit le sac parmi ceux de l'utilisateur, puis prépare le changement. Le solde sert seulement à la réponse.
    private async Task<PreparedChanges<BagRenameResult>> PrepareNewNameAsync(Guid userId, Guid bagId, string name)
    {
        var bag = await bagRepository.FindAsync(userId, bagId);

        if (bag == null)
        {
            return PreparedChanges<BagRenameResult>.NothingToSave(new BagRenameResult.NotFound());
        }

        var balances = await bagRepository.SumBalancesAsync(userId);
        bag.Rename(name);

        return PreparedChanges<BagRenameResult>.SaveThenReturn(
            new BagRenameResult.Renamed { Bag = BagView.Create(bag, balances.GetValueOrDefault(bag.Id)) });
    }
}
