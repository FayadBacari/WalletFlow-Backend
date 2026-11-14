using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Bags;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Lit et écrit les sacs et leurs mouvements dans la base PostgreSQL. Chaque requête filtre sur l'utilisateur de la session (SS §4).
// Les objets lus ici sont suivis par EF Core : l'unité de travail enregistre ce que leurs règles ont changé (voir IDatabaseUnitOfWork).
public class BagRepository : IBagRepository
{
    private readonly WalletFlowDatabaseContext databaseContext;

    // Reçoit l'accès à la base de la requête en cours.
    public BagRepository(WalletFlowDatabaseContext databaseContext)
    {
        this.databaseContext = databaseContext;
    }

    // Donne tous les sacs de l'utilisateur.
    public async Task<List<Bag>> FindAllAsync(Guid userId)
    {
        return await databaseContext.Bags
            .Where(bag => bag.UserId == userId)
            .ToListAsync();
    }

    // Cherche le sac par son identifiant et par son utilisateur : celui d'un autre n'est jamais trouvé.
    public async Task<Bag?> FindAsync(Guid userId, Guid bagId)
    {
        return await databaseContext.Bags.SingleOrDefaultAsync(bag =>
            bag.Id == bagId
            && bag.UserId == userId);
    }

    // Compte les sacs de l'utilisateur.
    public async Task<int> CountAsync(Guid userId)
    {
        return await databaseContext.Bags.CountAsync(bag => bag.UserId == userId);
    }

    // Additionne en SQL, pour chaque sac de l'utilisateur, les mouvements arrivés et les mouvements partis.
    // Les montants sont additionnés en bigint : beaucoup de mouvements ne débordent jamais.
    public async Task<Dictionary<Guid, long>> SumBalancesAsync(Guid userId)
    {
        var rows = await databaseContext.Bags
            .AsNoTracking()
            .Where(bag => bag.UserId == userId)
            .Select(bag => new BagBalanceRow
            {
                BagId = bag.Id,
                IncomingInCents = databaseContext.BagMovements
                    .Where(movement => movement.DestinationBagId == bag.Id)
                    .Sum(movement => (long)movement.AmountInCents),
                OutgoingInCents = databaseContext.BagMovements
                    .Where(movement => movement.SourceBagId == bag.Id)
                    .Sum(movement => (long)movement.AmountInCents)
            })
            .ToListAsync();

        var balances = new Dictionary<Guid, long>();

        foreach (var row in rows)
        {
            balances.Add(row.BagId, row.IncomingInCents - row.OutgoingInCents);
        }

        return balances;
    }

    // Compte les mouvements faits depuis cette date dont un côté est un sac de l'utilisateur.
    // Un mouvement dont l'autre sac a été supprimé reste compté avec le sac qui reste.
    // Les sacs sont lus d'abord : la recherche passe alors par les index des deux côtés d'un mouvement (ix_mouvement_depart
    // et ix_mouvement_arrivee), sans parcourir les mouvements de tous les utilisateurs à chaque déplacement.
    public async Task<int> CountMovementsSinceAsync(Guid userId, DateTime since)
    {
        var bagIds = await UserBagIds(userId).ToListAsync();

        return await databaseContext.BagMovements.CountAsync(movement =>
            movement.MovedAt >= since
            && ((movement.SourceBagId != null && bagIds.Contains(movement.SourceBagId.Value))
                || (movement.DestinationBagId != null && bagIds.Contains(movement.DestinationBagId.Value))));
    }

    // Lit en SQL la page demandée, avec le nom de l'autre sac de chaque mouvement. L'autre côté d'un mouvement est
    // le départ quand l'argent arrive dans ce sac, et l'arrivée quand il en part : null veut dire l'argent disponible.
    // Le sac doit être à l'utilisateur, sinon rien n'est trouvé.
    public async Task<BagHistoryPage> FindHistoryAsync(Guid userId, Guid bagId, int page, int pageSize)
    {
        var query = databaseContext.BagMovements
            .AsNoTracking()
            .Where(movement =>
                (movement.SourceBagId == bagId || movement.DestinationBagId == bagId)
                && UserBagIds(userId).Contains(bagId));

        var totalCount = await query.CountAsync();
        var rows = await query
            .OrderByDescending(movement => movement.MovedAt)
            .ThenByDescending(movement => movement.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(movement => new BagHistoryRow
            {
                Id = movement.Id,
                MovedAt = movement.MovedAt,
                AmountInCents = movement.AmountInCents,
                IsIncoming = movement.DestinationBagId == bagId,
                OtherBagId = movement.DestinationBagId == bagId ? movement.SourceBagId : movement.DestinationBagId,
                OtherBagName = databaseContext.Bags
                    .Where(otherBag =>
                        otherBag.Id == (movement.DestinationBagId == bagId ? movement.SourceBagId : movement.DestinationBagId))
                    .Select(otherBag => otherBag.Name)
                    .FirstOrDefault()
            })
            .ToListAsync();

        var items = new List<BagHistoryItem>();

        foreach (var row in rows)
        {
            var amountInCents = row.IsIncoming ? row.AmountInCents : -row.AmountInCents;
            items.Add(new BagHistoryItem(row.Id, row.MovedAt, amountInCents, row.OtherBagId, row.OtherBagName));
        }

        return new BagHistoryPage(items, totalCount);
    }

    // Lit la ligne de l'utilisateur et la suit : sa version (xmin) est gardée dès cette lecture.
    public async Task<User?> FindUserAsync(Guid userId)
    {
        return await databaseContext.Users.SingleOrDefaultAsync(user => user.Id == userId);
    }

    // La ligne de l'utilisateur sera écrite même si rien n'y a changé : cette écriture contrôle en base sa version,
    // et elle échoue si une autre requête l'a écrite depuis sa lecture. EF Core n'écrit une ligne que si une colonne
    // a changé : le compteur d'essais est donc réécrit avec sa propre valeur, comme dans UserAccountRepository.
    public void PrepareUserCheck(User user)
    {
        databaseContext.Entry(user).Property(nameof(User.FailedLoginAttemptCount)).IsModified = true;
    }

    // Garde de côté le nouveau sac : il ne sera écrit qu'avec les autres changements.
    public void PrepareBag(Bag bag)
    {
        databaseContext.Bags.Add(bag);
    }

    // Garde de côté le nouveau mouvement : il ne sera écrit qu'avec les autres changements.
    public void PrepareMovement(BagMovement movement)
    {
        databaseContext.BagMovements.Add(movement);
    }

    // Prépare la suppression du sac. EF Core ne touche à aucun mouvement : c'est la base qui applique ses règles,
    // avec le déclencheur tg_sac_avant_suppression et ON DELETE SET NULL (voir BagMovementTableConfiguration).
    public void PrepareDeletion(Bag bag)
    {
        databaseContext.Bags.Remove(bag);
    }

    // Les identifiants des sacs de l'utilisateur, en sous-requête SQL.
    private IQueryable<Guid> UserBagIds(Guid userId)
    {
        return databaseContext.Bags
            .Where(bag => bag.UserId == userId)
            .Select(bag => bag.Id);
    }

    // Une ligne lue en SQL : les deux sommes d'un sac.
    private class BagBalanceRow
    {
        public Guid BagId { get; init; }
        public long IncomingInCents { get; init; }
        public long OutgoingInCents { get; init; }
    }

    // Une ligne lue en SQL : un mouvement de l'historique, avant de recevoir son signe.
    private class BagHistoryRow
    {
        public Guid Id { get; init; }
        public DateTime MovedAt { get; init; }
        public int AmountInCents { get; init; }
        public bool IsIncoming { get; init; }
        public Guid? OtherBagId { get; init; }
        public string? OtherBagName { get; init; }
    }
}
