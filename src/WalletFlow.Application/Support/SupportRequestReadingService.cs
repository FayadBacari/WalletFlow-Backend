namespace WalletFlow.Application.Support;

// Cas d'utilisation : l'utilisateur connecté suit ses demandes de support (GR Admin) : leur liste, puis chacune avec ses réponses.
// Il ne voit que les siennes (SS §4).
public class SupportRequestReadingService
{
    private readonly ISupportRequestRepository supportRepository;

    // Reçoit l'accès aux demandes.
    public SupportRequestReadingService(ISupportRequestRepository supportRepository)
    {
        this.supportRepository = supportRepository;
    }

    // Donne les demandes de l'utilisateur, la plus récente d'abord.
    public async Task<List<SupportRequestSummary>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await supportRepository.FindUserRequestsAsync(userId, cancellationToken);
    }

    // Donne une demande de l'utilisateur avec ses réponses, ou null si elle n'existe pas ou n'est pas à lui :
    // les deux cas se ressemblent, pour ne pas révéler qu'une demande existe (SS §4).
    public async Task<SupportRequestDetails?> ReadAsync(Guid userId, Guid requestId, CancellationToken cancellationToken = default)
    {
        return await supportRepository.FindUserRequestAsync(userId, requestId, cancellationToken);
    }
}
