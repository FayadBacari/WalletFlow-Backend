namespace WalletFlow.Application.Security;

// Retrouve à qui appartient une ressource, quel que soit l'utilisateur de la session. Implémenté dans la couche Infrastructure.
// Sert seulement à noter au journal une tentative d'accès à la ressource d'un autre (SS §11), jamais à donner un accès :
// les cas d'utilisation, eux, ne cherchent toujours que parmi les ressources de l'utilisateur (SS §4).
public interface IResourceOwnershipRepository
{
    // Donne l'utilisateur à qui appartient cette ressource, ou null si elle n'existe pas.
    Task<Guid?> FindOwnerAsync(OwnedResource resource, Guid resourceId);
}
