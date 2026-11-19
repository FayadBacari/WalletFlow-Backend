using WalletFlow.Application.Security;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : retrouve le propriétaire d'une ressource dans les autres faux dépôts.
// Les vraies requêtes sont testées sur la vraie base (ResourceOwnershipRepositoryTests).
public class FakeResourceOwnershipRepository : IResourceOwnershipRepository
{
    private readonly FakeCategoryRepository categoryRepository;
    private readonly FakeBankConnectionRepository bankRepository;
    private readonly FakeBagRepository bagRepository;
    private readonly FakeSuggestionRepository? suggestionRepository;
    private readonly FakeSupportRequestRepository? supportRepository;

    // Reçoit les faux dépôts où chercher : les catégories et les opérations, les connexions et leurs comptes, les sacs,
    // et les suggestions et les demandes de support si le test en a.
    public FakeResourceOwnershipRepository(
        FakeCategoryRepository categoryRepository,
        FakeBankConnectionRepository bankRepository,
        FakeBagRepository bagRepository,
        FakeSuggestionRepository? suggestionRepository = null,
        FakeSupportRequestRepository? supportRepository = null)
    {
        this.categoryRepository = categoryRepository;
        this.bankRepository = bankRepository;
        this.bagRepository = bagRepository;
        this.suggestionRepository = suggestionRepository;
        this.supportRepository = supportRepository;
    }

    // Cherche la ressource dans le faux dépôt de sa sorte. Un compte et une opération sont à l'utilisateur de leur connexion.
    public Task<Guid?> FindOwnerAsync(OwnedResource resource, Guid resourceId, CancellationToken cancellationToken = default)
    {
        Guid? accountId = null;

        switch (resource)
        {
            case OwnedResource.Category:
                return Task.FromResult(categoryRepository.SavedCategories.SingleOrDefault(category => category.Id == resourceId)?.UserId);
            case OwnedResource.Suggestion:
                return Task.FromResult(suggestionRepository?.SavedSuggestions.SingleOrDefault(suggestion => suggestion.Id == resourceId)?.UserId);
            case OwnedResource.SupportRequest:
                return Task.FromResult(supportRepository?.SavedRequests.SingleOrDefault(request => request.Id == resourceId)?.UserId);
            case OwnedResource.Bag:
                return Task.FromResult(bagRepository.SavedBags.SingleOrDefault(bag => bag.Id == resourceId)?.UserId);
            case OwnedResource.BankConnection:
                return Task.FromResult(bankRepository.SavedConnections.SingleOrDefault(connection => connection.Id == resourceId)?.UserId);
            case OwnedResource.BankAccount:
                accountId = resourceId;
                break;
            default:
                accountId = categoryRepository.SavedTransactions.SingleOrDefault(transaction => transaction.Id == resourceId)?.BankAccountId;
                break;
        }

        var account = bankRepository.SavedAccounts.SingleOrDefault(saved => saved.Id == accountId);
        var connection = bankRepository.SavedConnections.SingleOrDefault(saved => saved.Id == account?.BankConnectionId);

        return Task.FromResult(connection?.UserId);
    }
}
