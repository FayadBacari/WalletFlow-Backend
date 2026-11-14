using WalletFlow.Application.Security;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : retrouve le propriétaire d'une ressource dans les autres faux dépôts.
// Les vraies requêtes sont testées sur la vraie base (ResourceOwnershipRepositoryTests).
public class FakeResourceOwnershipRepository : IResourceOwnershipRepository
{
    private readonly FakeCategoryRepository categoryRepository;
    private readonly FakeBankConnectionRepository bankRepository;
    private readonly FakeBagRepository bagRepository;

    // Reçoit les faux dépôts où chercher : les catégories et les opérations, les connexions et leurs comptes, les sacs.
    public FakeResourceOwnershipRepository(
        FakeCategoryRepository categoryRepository,
        FakeBankConnectionRepository bankRepository,
        FakeBagRepository bagRepository)
    {
        this.categoryRepository = categoryRepository;
        this.bankRepository = bankRepository;
        this.bagRepository = bagRepository;
    }

    // Cherche la ressource dans le faux dépôt de sa sorte. Un compte et une opération sont à l'utilisateur de leur connexion.
    public Task<Guid?> FindOwnerAsync(OwnedResource resource, Guid resourceId)
    {
        Guid? accountId = null;

        switch (resource)
        {
            case OwnedResource.Category:
                return Task.FromResult(categoryRepository.SavedCategories.SingleOrDefault(category => category.Id == resourceId)?.UserId);
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
