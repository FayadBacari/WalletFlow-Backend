using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Bags;

// Accès aux sacs des utilisateurs et à leurs mouvements. Implémenté dans la couche Infrastructure.
// Chaque recherche reçoit l'utilisateur de la session : le sac d'un autre n'est jamais trouvé (SS §4).
// Les objets lus sont suivis, et les méthodes « Prepare… » n'écrivent rien (voir IDatabaseUnitOfWork).
public interface IBagRepository
{
    // Donne tous les sacs de l'utilisateur, dans aucun ordre particulier.
    Task<List<Bag>> FindAllAsync(Guid userId);

    // Cherche ce sac parmi ceux de l'utilisateur. Renvoie null s'il n'existe pas, ou s'il est à un autre.
    Task<Bag?> FindAsync(Guid userId, Guid bagId);

    // Compte les sacs de l'utilisateur.
    Task<int> CountAsync(Guid userId);

    // Donne le solde de chaque sac de l'utilisateur (DD §11) : les mouvements arrivés dans le sac moins ceux qui en sont partis.
    // Chaque sac de l'utilisateur y est, avec 0 s'il n'a aucun mouvement. Le sac d'un autre n'y est jamais.
    Task<Dictionary<Guid, long>> SumBalancesAsync(Guid userId);

    // Compte les mouvements des sacs de l'utilisateur faits depuis cette date.
    Task<int> CountMovementsSinceAsync(Guid userId, DateTime since);

    // Donne une page de l'historique de ce sac de l'utilisateur, les mouvements les plus récents d'abord,
    // et le nombre de tous ses mouvements. La page est vide si le sac n'est pas à lui.
    Task<BagHistoryPage> FindHistoryAsync(Guid userId, Guid bagId, int page, int pageSize);

    // Lit la ligne de l'utilisateur et la suit. Elle doit être lue avant tout ce que les contrôles lisent (voir PrepareUserCheck).
    // Renvoie null si son compte n'existe plus.
    Task<User?> FindUserAsync(Guid userId);

    // La ligne de l'utilisateur sera écrite même si rien n'y a changé : l'enregistrement échoue alors si une autre requête
    // l'a écrite depuis sa lecture. Les créations de sacs et les déplacements d'argent d'un même utilisateur passent ainsi
    // un par un : deux requêtes ne peuvent pas passer ensemble le contrôle du nombre de sacs ou celui des montants (SS §5).
    void PrepareUserCheck(User user);

    // Garde de côté un nouveau sac : il ne sera enregistré qu'avec les autres changements.
    void PrepareBag(Bag bag);

    // Garde de côté un nouveau mouvement : il ne sera enregistré qu'avec les autres changements.
    void PrepareMovement(BagMovement movement);

    // Prépare la suppression de ce sac, lu par FindAsync. À l'enregistrement, la base remet son argent dans l'argent disponible :
    // ses mouvements avec l'argent disponible disparaissent, et ceux avec un autre sac gardent l'autre sac (MPD).
    void PrepareDeletion(Bag bag);
}
