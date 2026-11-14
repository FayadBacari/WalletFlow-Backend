using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Application.Bags;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Courses entre requêtes simultanées sur les sacs, avec les vrais cas d'utilisation et la vraie base de test :
// chaque requête a son propre accès à la base et sa propre transaction, comme dans l'API (SS §5 et §6).
[Collection(RealDatabaseCollection.Name)]
public class ConcurrentBagRequestsTests
{
    private const int SimultaneousRequestCount = 20;

    // Enregistre pour de vrai un compte confirmé, avec un compte bancaire de ce solde.
    private static async Task<User> SaveAccountWithBalanceAsync(string email, int balanceInCents)
    {
        var account = new User { Email = email, PasswordHash = "empreinte-de-test", IsEmailConfirmed = true };
        var connection = new BankConnection
        {
            ExternalConnectionId = "connexion-" + Guid.NewGuid(),
            BankName = "Banque de test",
            AuthorizationExpiresAt = DateTime.UtcNow.AddDays(180),
            UserId = account.Id
        };
        var bankAccount = new BankAccount
        {
            ExternalAccountId = "compte-" + Guid.NewGuid(),
            Name = "Compte courant",
            BalanceInCents = balanceInCents,
            BalanceUpdatedAt = DateTime.UtcNow,
            BankConnectionId = connection.Id
        };

        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.AddRange(account, connection, bankAccount);
        await databaseContext.SaveChangesAsync();

        return account;
    }

    // Enregistre pour de vrai ce nombre de sacs pour ce compte, et les renvoie.
    private static async Task<List<Bag>> SaveBagsAsync(Guid userId, int count)
    {
        var bags = new List<Bag>();

        for (var number = 1; number <= count; number++)
        {
            bags.Add(new Bag { Name = "Sac " + number, UserId = userId });
        }

        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.Bags.AddRange(bags);
        await databaseContext.SaveChangesAsync();

        return bags;
    }

    // Enregistre pour de vrai ce mouvement, fait maintenant, et le renvoie.
    private static async Task<BagMovement> SaveMovementAsync(Bag? source, Bag? destination, int amountInCents)
    {
        var movement = new BagMovement
        {
            AmountInCents = amountInCents,
            MovedAt = DateTime.UtcNow,
            SourceBagId = source?.Id,
            DestinationBagId = destination?.Id
        };

        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.BagMovements.Add(movement);
        await databaseContext.SaveChangesAsync();

        return movement;
    }

    // Lit en base le solde de chaque sac du compte.
    private static async Task<Dictionary<Guid, long>> ReadBalancesAsync(Guid userId)
    {
        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();

        return await new BagRepository(databaseContext).SumBalancesAsync(userId);
    }

    // Déplace de l'argent avec le vrai cas d'utilisation, dans la portée de la requête.
    private static async Task<BagMovementResult> MoveAsync(IServiceProvider services, Guid userId, Guid? sourceBagId, Guid? destinationBagId, int amountInCents)
    {
        return await services.GetRequiredService<BagMovementService>().MoveAsync(userId, sourceBagId, destinationBagId, amountInCents);
    }

    // SS §5 : 20 ajouts de 20 000 centimes en même temps, dans 20 sacs différents, pour 100 000 centimes disponibles.
    // Exactement 5 passent, et les sacs tiennent tout juste le solde total. Sans le contrôle de la ligne de l'utilisateur,
    // chaque requête verrait 100 000 centimes disponibles, et les 20 passeraient.
    [Fact]
    public async Task SimultaneousAdditionsNeverGoBeyondTheTotalBalance()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-sacs-ajouts");

        try
        {
            var account = await SaveAccountWithBalanceAsync(email, 100_000);
            var bags = await SaveBagsAsync(account.Id, SimultaneousRequestCount);

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, requestNumber) =>
                MoveAsync(services, account.Id, null, bags[requestNumber].Id, 20_000));

            Assert.Equal(5, results.Count(result => result is BagMovementResult.Moved));
            Assert.Equal(15, results.Count(result => result is BagMovementResult.AvailableMoneyTooLow));
            Assert.Equal(100_000, (await ReadBalancesAsync(account.Id)).Values.Sum());
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // GR Sacs : 20 retraits de 20 000 centimes en même temps, d'un sac qui en contient 100 000, vers l'argent disponible
    // ou vers d'autres sacs. Exactement 5 passent, et le sac finit vide, jamais négatif.
    [Fact]
    public async Task SimultaneousWithdrawalsNeverMakeABagNegative()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-sacs-retraits");

        try
        {
            var account = await SaveAccountWithBalanceAsync(email, 100_000);
            var bags = await SaveBagsAsync(account.Id, SimultaneousRequestCount);
            var emptiedBag = bags[0];
            await SaveMovementAsync(null, emptiedBag, 100_000);

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, requestNumber) =>
            {
                Guid? destinationBagId = requestNumber % 2 == 0 ? null : bags[requestNumber].Id;
                return MoveAsync(services, account.Id, emptiedBag.Id, destinationBagId, 20_000);
            });

            var balances = await ReadBalancesAsync(account.Id);
            Assert.Equal(5, results.Count(result => result is BagMovementResult.Moved));
            Assert.Equal(15, results.Count(result => result is BagMovementResult.SourceBagTooLow));
            Assert.Equal(0, balances[emptiedBag.Id]);
            Assert.All(balances.Values, balance => Assert.True(balance >= 0));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // SS §6 : 20 créations en même temps, à 95 sacs. Exactement 5 passent, et le compte finit avec 100 sacs.
    [Fact]
    public async Task SimultaneousCreationsNeverGoBeyondTheLimit()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-sacs-limite");

        try
        {
            var account = await SaveAccountWithBalanceAsync(email, 100_000);
            await SaveBagsAsync(account.Id, 95);

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, requestNumber) =>
                services.GetRequiredService<BagCreationService>().CreateAsync(account.Id, "Nouveau sac " + requestNumber, null, null));

            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            Assert.Equal(5, results.Count(result => result is BagCreationResult.Created));
            Assert.Equal(15, results.Count(result => result is BagCreationResult.TooManyBags));
            Assert.Equal(BagRules.MaximumCountPerUser, await databaseContext.Bags.CountAsync(row => row.UserId == account.Id));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // SS §6 : 20 déplacements en même temps, à 995 déplacements sur les dernières 24 heures. Exactement 5 passent.
    [Fact]
    public async Task SimultaneousMovementsNeverGoBeyondTheDailyLimit()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-sacs-deplacements");

        try
        {
            var account = await SaveAccountWithBalanceAsync(email, 100_000);
            var bags = await SaveBagsAsync(account.Id, 1);
            await using (var databaseContext = LocalTestDatabase.CreateDatabaseContext())
            {
                await databaseContext.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO mouvement_sac (id_mouvement, montant_mouvement, date_mouvement, id_sac_arrivee)
                    SELECT gen_random_uuid(), 1, now(), {bags[0].Id} FROM generate_series(1, 995)
                    """);
            }

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, requestNumber) =>
                MoveAsync(services, account.Id, null, bags[0].Id, 1));

            Assert.Equal(5, results.Count(result => result is BagMovementResult.Moved));
            Assert.Equal(15, results.Count(result => result is BagMovementResult.TooManyMovements));
            Assert.Equal(1000, (await ReadBalancesAsync(account.Id))[bags[0].Id]);
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // 10 paires de sacs reliées par des mouvements dans les deux sens, toutes supprimées en même temps, plusieurs fois.
    // Deux suppressions d'une même paire se gênent dans la base (mouvement vidé des deux côtés, ou interblocage) :
    // la seconde recommence. Toutes finissent par réussir, sans panne, et il ne reste ni sac ni mouvement.
    [Fact]
    public async Task LinkedBagsDeletedAtTheSameMomentAreAllDeleted()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-sacs-suppressions");

        try
        {
            var account = await SaveAccountWithBalanceAsync(email, 1_000_000);
            var movementIds = new List<Guid>();

            for (var round = 1; round <= 5; round++)
            {
                var bags = await SaveBagsAsync(account.Id, SimultaneousRequestCount);

                for (var pair = 0; pair < SimultaneousRequestCount; pair = pair + 2)
                {
                    movementIds.Add((await SaveMovementAsync(null, bags[pair], 10_000)).Id);
                    movementIds.Add((await SaveMovementAsync(bags[pair], bags[pair + 1], 3000)).Id);
                    movementIds.Add((await SaveMovementAsync(bags[pair + 1], bags[pair], 1000)).Id);
                }

                var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, requestNumber) =>
                    services.GetRequiredService<BagDeletionService>().DeleteAsync(account.Id, bags[requestNumber].Id));

                Assert.All(results, result => Assert.IsType<BagDeletionResult.Deleted>(result));
            }

            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            Assert.False(await databaseContext.Bags.AnyAsync(row => row.UserId == account.Id));
            Assert.Equal(150, movementIds.Count);
            Assert.False(await databaseContext.BagMovements.AnyAsync(row => movementIds.Contains(row.Id)));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // Déplacements et suppressions mélangés sur les mêmes sacs : chaque requête finit par une réponse prévue, jamais par une panne,
    // et les règles tiennent à la fin : aucun sac négatif, et les sacs ne dépassent pas le solde total. « Réessaie » est
    // une réponse prévue : un déplacement qui perd 10 fois de suite contre les autres est refusé, jamais fait sans contrôle.
    [Fact]
    public async Task MovementsAndDeletionsAtTheSameMomentKeepTheRules()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-sacs-melange");

        try
        {
            var account = await SaveAccountWithBalanceAsync(email, 100_000);

            for (var round = 1; round <= 5; round++)
            {
                var bags = await SaveBagsAsync(account.Id, 4);
                await SaveMovementAsync(null, bags[0], 20_000);
                await SaveMovementAsync(null, bags[1], 20_000);

                var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, async (services, requestNumber) =>
                {
                    var from = bags[requestNumber % 4];
                    var to = bags[(requestNumber + 1) % 4];

                    if (requestNumber % 5 == 0)
                    {
                        return (object)await services.GetRequiredService<BagDeletionService>().DeleteAsync(account.Id, from.Id);
                    }

                    if (requestNumber % 5 == 1)
                    {
                        return await MoveAsync(services, account.Id, null, to.Id, 15_000);
                    }

                    if (requestNumber % 5 == 2)
                    {
                        return await MoveAsync(services, account.Id, from.Id, null, 5000);
                    }

                    return await MoveAsync(services, account.Id, from.Id, to.Id, 7000);
                });

                Assert.All(results, result => Assert.True(
                    result is BagDeletionResult.Deleted
                    || result is BagDeletionResult.NotFound
                    || result is BagDeletionResult.NotSaved
                    || result is BagMovementResult.NotSaved
                    || result is BagMovementResult.Moved
                    || result is BagMovementResult.NotFound
                    || result is BagMovementResult.SourceBagTooLow
                    || result is BagMovementResult.AvailableMoneyTooLow,
                    "Réponse inattendue : " + result.GetType().Name));

                var balances = await ReadBalancesAsync(account.Id);
                Assert.All(balances.Values, balance => Assert.True(balance >= 0));
                Assert.True(balances.Values.Sum() <= 100_000);
            }
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }
}
