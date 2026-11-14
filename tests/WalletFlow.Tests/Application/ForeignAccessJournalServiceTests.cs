using System.Net;
using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le journal des tentatives d'accès à une ressource d'un autre utilisateur (SS §11).
public class ForeignAccessJournalServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly IPAddress VisitorAddress = IPAddress.Parse("203.0.113.7");

    // Un faux propriétaire : chaque ressource connue a l'utilisateur qu'on lui donne, les autres n'existent pas.
    private sealed class OwnersByResource : IResourceOwnershipRepository
    {
        public Dictionary<Guid, Guid> Owners { get; } = new Dictionary<Guid, Guid>();
        public List<OwnedResource> SearchedResources { get; } = new List<OwnedResource>();

        // Donne le propriétaire gardé pour cette ressource, et note la sorte de ressource cherchée.
        public Task<Guid?> FindOwnerAsync(OwnedResource resource, Guid resourceId)
        {
            SearchedResources.Add(resource);

            if (Owners.TryGetValue(resourceId, out var ownerId))
            {
                return Task.FromResult<Guid?>(ownerId);
            }

            return Task.FromResult<Guid?>(null);
        }
    }

    // Crée le service sur ces faux propriétaires et ce faux journal.
    private static ForeignAccessJournalService CreateService(OwnersByResource owners, FakeSecurityEventRepository journal)
    {
        return new ForeignAccessJournalService(owners, new SecurityJournalService(journal, new FakeTimeProvider()));
    }

    [Fact]
    public async Task ResourceOfAnotherUserIsJournaledAsAnAccessDenied()
    {
        var owners = new OwnersByResource();
        var journal = new FakeSecurityEventRepository();
        var bagOfAnotherUser = Guid.NewGuid();
        owners.Owners[bagOfAnotherUser] = Guid.NewGuid();

        await CreateService(owners, journal).RecordIfForeignAsync(UserId, VisitorAddress, OwnedResource.Bag, bagOfAnotherUser);

        var securityEvent = Assert.Single(journal.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.AccessDenied, securityEvent.Type);
        Assert.Equal(SecurityEventResult.Failure, securityEvent.Result);
        Assert.Equal(UserId, securityEvent.UserId);
        Assert.Equal(VisitorAddress, securityEvent.IpAddress);
        Assert.Equal(new List<OwnedResource> { OwnedResource.Bag }, owners.SearchedResources);
    }

    // Une ressource à l'utilisateur lui-même, une ressource inconnue, ou aucun identifiant : rien n'est noté.
    [Fact]
    public async Task OwnUnknownOrMissingResourceLeavesNothing()
    {
        var owners = new OwnersByResource();
        var journal = new FakeSecurityEventRepository();
        var ownCategory = Guid.NewGuid();
        owners.Owners[ownCategory] = UserId;
        var service = CreateService(owners, journal);

        await service.RecordIfForeignAsync(UserId, VisitorAddress, OwnedResource.Category, ownCategory);
        await service.RecordIfForeignAsync(UserId, VisitorAddress, OwnedResource.Category, Guid.NewGuid());
        await service.RecordIfForeignAsync(UserId, VisitorAddress, OwnedResource.Category, null);

        Assert.Empty(journal.SavedSecurityEvents);
        Assert.Equal(2, owners.SearchedResources.Count);
    }
}
