namespace WalletFlow.Application.Bags;

// Cas d'utilisation : l'utilisateur connecté voit l'historique des mouvements d'un de ses sacs (GR Sacs),
// les plus récents d'abord, 50 par page.
public class BagHistoryService
{
    public const int PageSize = 50;
    public const int MaximumPage = 10_000;

    private readonly IBagRepository bagRepository;

    // Reçoit l'accès aux sacs.
    public BagHistoryService(IBagRepository bagRepository)
    {
        this.bagRepository = bagRepository;
    }

    // Donne la page demandée de l'historique du sac, s'il est à l'utilisateur.
    public async Task<BagHistoryResult> ReadAsync(Guid userId, Guid bagId, int page)
    {
        if (page < 1 || page > MaximumPage)
        {
            return new BagHistoryResult.PageBreaksRules();
        }

        if (await bagRepository.FindAsync(userId, bagId) == null)
        {
            return new BagHistoryResult.NotFound();
        }

        return new BagHistoryResult.Found { Page = await bagRepository.FindHistoryAsync(userId, bagId, page, PageSize) };
    }
}
