using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Suggestions;

// Accès aux suggestions d'épargne des utilisateurs. Implémenté dans la couche Infrastructure.
// Chaque recherche reçoit l'utilisateur de la session : la suggestion d'un autre n'est jamais trouvée (SS §4).
// Les objets lus sont suivis, et les méthodes « Prepare… » n'écrivent rien (voir IDatabaseUnitOfWork).
// La ligne de l'utilisateur est lue et contrôlée par IBagRepository : une réponse à une suggestion déplace de l'argent
// dans un sac, et passe ainsi un par un avec les déplacements du même utilisateur.
public interface ISuggestionRepository
{
    // Donne toutes les suggestions de l'utilisateur, de tous les mois et de tous les états, dans aucun ordre particulier.
    Task<List<SavingsSuggestion>> FindAllAsync(Guid userId, CancellationToken cancellationToken = default);

    // Cherche cette suggestion parmi celles de l'utilisateur. Renvoie null si elle n'existe pas, ou si elle est à un autre.
    Task<SavingsSuggestion?> FindAsync(Guid userId, Guid suggestionId, CancellationToken cancellationToken = default);

    // Donne, dans l'ordre de leur identifiant, au plus ce nombre d'utilisateurs après celui-ci (tous si null) dont ce mois
    // attend son calcul : compte confirmé et pas en suppression, au moins une opération, et un dernier mois calculé vide ou plus
    // ancien. Rien n'est suivi.
    Task<List<Guid>> FindUsersToCalculateAsync(DateOnly month, Guid? afterUserId, int count, CancellationToken cancellationToken = default);

    // Additionne les mouvements créés en acceptant les suggestions de l'utilisateur (DD §11, economies_realisees).
    // Un mouvement effacé avec son sac n'y est plus : son argent est revenu dans l'argent disponible.
    Task<long> SumSavedAsync(Guid userId, CancellationToken cancellationToken = default);

    // Garde de côté une nouvelle suggestion : elle ne sera enregistrée qu'avec les autres changements.
    void PrepareSuggestion(SavingsSuggestion suggestion);

    // Prépare l'effacement de cette suggestion, lue par FindAllAsync. Le mouvement qu'elle a pu créer reste dans l'historique
    // du sac (ON DELETE SET NULL).
    void PrepareDeletion(SavingsSuggestion suggestion);
}
