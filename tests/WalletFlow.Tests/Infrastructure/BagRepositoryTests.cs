using Microsoft.EntityFrameworkCore;
using Npgsql;
using WalletFlow.Application.Bags;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Tests.EndToEnd;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les sacs dans la vraie base PostgreSQL locale, avec l'utilisateur de l'API (GR Sacs, SS §4, §5 et §6, MPD).
// Sauf mention contraire, chaque test travaille dans une transaction annulée à la fin : la base reste vide.
public class BagRepositoryTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(30);

    // Enregistre ces sacs, puis oublie les objets gardés en mémoire.
    private static async Task SaveBagsAsync(WalletFlowDatabaseContext databaseContext, params Bag[] bags)
    {
        databaseContext.Bags.AddRange(bags);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();
    }

    // Enregistre un mouvement, daté de maintenant si le test ne donne pas de date, puis oublie les objets gardés en mémoire.
    private static async Task<BagMovement> SaveMovementAsync(
        WalletFlowDatabaseContext databaseContext,
        Bag? source,
        Bag? destination,
        int amountInCents,
        DateTime? movedAt = null)
    {
        var movement = new BagMovement
        {
            AmountInCents = amountInCents,
            MovedAt = movedAt ?? Now,
            SourceBagId = source?.Id,
            DestinationBagId = destination?.Id
        };
        databaseContext.BagMovements.Add(movement);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        return movement;
    }

    // --- Contrôle d'accès (SS §4)

    // Chaque recherche ne voit que les sacs de l'utilisateur : le sac d'un autre n'est jamais trouvé, même avec son identifiant.
    [Fact]
    public async Task SearchesSeeOnlyTheBagsOfTheUser()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveConfirmedAccountAsync(databaseContext, "test-sacs-acces@exemple.test");
        var otherUser = await SaveConfirmedAccountAsync(databaseContext, "test-sacs-acces-autre@exemple.test");
        var holidays = new Bag { Name = "Vacances", UserId = user.Id };
        var otherUserBag = new Bag { Name = "Sac d'un autre", UserId = otherUser.Id };
        await SaveBagsAsync(databaseContext, holidays, otherUserBag);
        var repository = new BagRepository(databaseContext);

        Assert.Null(await repository.FindAsync(user.Id, otherUserBag.Id));
        Assert.Equal(holidays.Id, (await repository.FindAsync(user.Id, holidays.Id))?.Id);
        Assert.Equal(new List<Guid> { holidays.Id }, (await repository.FindAllAsync(user.Id)).Select(bag => bag.Id).ToList());
        Assert.Equal(1, await repository.CountAsync(user.Id));
    }

    // --- Soldes (DD §11)

    // Le solde est ce qui est arrivé moins ce qui est parti. Un sac sans mouvement a 0, le sac d'un autre n'y est pas.
    [Fact]
    public async Task BalancesAddArrivalsAndRemoveDepartures()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveConfirmedAccountAsync(databaseContext, "test-sacs-soldes@exemple.test");
        var otherUser = await SaveConfirmedAccountAsync(databaseContext, "test-sacs-soldes-autre@exemple.test");
        var holidays = new Bag { Name = "Vacances", UserId = user.Id };
        var savings = new Bag { Name = "Épargne", UserId = user.Id };
        var empty = new Bag { Name = "Vide", UserId = user.Id };
        var otherUserBag = new Bag { Name = "Sac d'un autre", UserId = otherUser.Id };
        await SaveBagsAsync(databaseContext, holidays, savings, empty, otherUserBag);
        await SaveMovementAsync(databaseContext, null, holidays, 50_000);
        await SaveMovementAsync(databaseContext, holidays, savings, 20_000);
        await SaveMovementAsync(databaseContext, savings, null, 5000);
        await SaveMovementAsync(databaseContext, null, otherUserBag, 9999);

        var balances = await new BagRepository(databaseContext).SumBalancesAsync(user.Id);

        Assert.Equal(3, balances.Count);
        Assert.Equal(30_000, balances[holidays.Id]);
        Assert.Equal(15_000, balances[savings.Id]);
        Assert.Equal(0, balances[empty.Id]);
        Assert.False(balances.ContainsKey(otherUserBag.Id));
    }

    // Les montants sont additionnés en bigint : trois fois le plus grand montant dépassent un int sans déborder.
    [Fact]
    public async Task BalancesAreAddedWithoutOverflow()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveConfirmedAccountAsync(databaseContext, "test-sacs-grands-soldes@exemple.test");
        var holidays = new Bag { Name = "Vacances", UserId = user.Id };
        await SaveBagsAsync(databaseContext, holidays);

        for (var number = 1; number <= 3; number++)
        {
            await SaveMovementAsync(databaseContext, null, holidays, BagRules.MaximumAmountInCents);
        }

        var balances = await new BagRepository(databaseContext).SumBalancesAsync(user.Id);

        Assert.Equal(3_000_000_000L, balances[holidays.Id]);
    }

    // --- Nombre de déplacements (SS §6)

    // Un mouvement compte une fois, même entre deux sacs de l'utilisateur. Celui fait juste avant la date ne compte pas,
    // celui fait à la date compte. Un retrait compte par son sac de départ, un ajout par son sac d'arrivée. Un mouvement
    // dont l'autre sac a été supprimé compte encore avec le sac qui reste.
    [Fact]
    public async Task MovementsSinceADateAreCountedOnceEach()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveConfirmedAccountAsync(databaseContext, "test-sacs-compte@exemple.test");
        var otherUser = await SaveConfirmedAccountAsync(databaseContext, "test-sacs-compte-autre@exemple.test");
        var holidays = new Bag { Name = "Vacances", UserId = user.Id };
        var savings = new Bag { Name = "Épargne", UserId = user.Id };
        var deleted = new Bag { Name = "Supprimé", UserId = user.Id };
        var otherUserBag = new Bag { Name = "Sac d'un autre", UserId = otherUser.Id };
        await SaveBagsAsync(databaseContext, holidays, savings, deleted, otherUserBag);
        var since = Now.AddHours(-24);
        await SaveMovementAsync(databaseContext, null, holidays, 1000, since.AddTicks(-10));
        await SaveMovementAsync(databaseContext, null, holidays, 1000, since);
        await SaveMovementAsync(databaseContext, holidays, savings, 500, Now);
        await SaveMovementAsync(databaseContext, savings, null, 200, Now);
        await SaveMovementAsync(databaseContext, null, deleted, 800, Now);
        await SaveMovementAsync(databaseContext, deleted, savings, 300, Now);
        await SaveMovementAsync(databaseContext, null, otherUserBag, 700, Now);
        databaseContext.Bags.Remove(await databaseContext.Bags.SingleAsync(row => row.Id == deleted.Id));
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        var count = await new BagRepository(databaseContext).CountMovementsSinceAsync(user.Id, since);

        Assert.Equal(4, count);
    }

    // --- Historique

    // Les plus récents d'abord, puis par identifiant à la même date ; le montant porte le signe vu depuis le sac.
    [Fact]
    public async Task HistoryGivesTheNewestFirstWithTheOtherSide()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveConfirmedAccountAsync(databaseContext, "test-sacs-historique@exemple.test");
        var holidays = new Bag { Name = "Vacances", UserId = user.Id };
        var savings = new Bag { Name = "Épargne", UserId = user.Id };
        await SaveBagsAsync(databaseContext, holidays, savings);
        var arrival = await SaveMovementAsync(databaseContext, null, holidays, 50_000, Now.AddHours(-3));
        var transfer = await SaveMovementAsync(databaseContext, holidays, savings, 20_000, Now.AddHours(-2));
        var withdrawal = await SaveMovementAsync(databaseContext, holidays, null, 5000, Now.AddHours(-1));
        var gift = await SaveMovementAsync(databaseContext, savings, holidays, 1000, Now.AddHours(-1));
        await SaveMovementAsync(databaseContext, null, savings, 3000, Now);
        var sameTimeIds = new List<Guid> { withdrawal.Id, gift.Id };
        sameTimeIds.Sort();
        sameTimeIds.Reverse();

        var page = await new BagRepository(databaseContext).FindHistoryAsync(user.Id, holidays.Id, 1, 50);

        Assert.Equal(4, page.TotalCount);
        Assert.Equal(
            new List<Guid> { sameTimeIds[0], sameTimeIds[1], transfer.Id, arrival.Id },
            page.Items.Select(item => item.Id).ToList());
        var giftItem = page.Items.Single(item => item.Id == gift.Id);
        var withdrawalItem = page.Items.Single(item => item.Id == withdrawal.Id);
        var transferItem = page.Items.Single(item => item.Id == transfer.Id);
        var arrivalItem = page.Items.Single(item => item.Id == arrival.Id);
        Assert.Equal(1000, giftItem.AmountInCents);
        Assert.Equal(savings.Id, giftItem.OtherBagId);
        Assert.Equal("Épargne", giftItem.OtherBagName);
        Assert.Equal(-5000, withdrawalItem.AmountInCents);
        Assert.Null(withdrawalItem.OtherBagId);
        Assert.Null(withdrawalItem.OtherBagName);
        Assert.Equal(-20_000, transferItem.AmountInCents);
        Assert.Equal(savings.Id, transferItem.OtherBagId);
        Assert.Equal(50_000, arrivalItem.AmountInCents);
        Assert.Null(arrivalItem.OtherBagId);
        Assert.Equal(Now.AddHours(-3), arrivalItem.MovedAt);
        Assert.Equal(DateTimeKind.Utc, arrivalItem.MovedAt.Kind);
    }

    // La deuxième page commence où la première s'arrête.
    [Fact]
    public async Task HistoryIsReadPageByPage()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveConfirmedAccountAsync(databaseContext, "test-sacs-pages@exemple.test");
        var holidays = new Bag { Name = "Vacances", UserId = user.Id };
        await SaveBagsAsync(databaseContext, holidays);

        for (var minute = 0; minute < 5; minute++)
        {
            await SaveMovementAsync(databaseContext, null, holidays, 100 + minute, Now.AddMinutes(-minute));
        }

        var secondPage = await new BagRepository(databaseContext).FindHistoryAsync(user.Id, holidays.Id, 2, 2);

        Assert.Equal(5, secondPage.TotalCount);
        Assert.Equal(new List<int> { 102, 103 }, secondPage.Items.Select(item => item.AmountInCents).ToList());
    }

    // SS §4 : l'historique du sac d'un autre utilisateur est vide, même avec son identifiant.
    [Fact]
    public async Task HistoryOfAnotherUserBagIsEmpty()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var user = await SaveConfirmedAccountAsync(databaseContext, "test-sacs-historique-intrus@exemple.test");
        var otherUser = await SaveConfirmedAccountAsync(databaseContext, "test-sacs-historique-autre@exemple.test");
        var otherUserBag = new Bag { Name = "Sac d'un autre", UserId = otherUser.Id };
        await SaveBagsAsync(databaseContext, otherUserBag);
        await SaveMovementAsync(databaseContext, null, otherUserBag, 5000);

        var page = await new BagRepository(databaseContext).FindHistoryAsync(user.Id, otherUserBag.Id, 1, 50);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    // --- Contrôle de la ligne de l'utilisateur (SS §5)

    // Une autre requête écrit la ligne de l'utilisateur entre sa lecture et l'enregistrement : rien n'est enregistré.
    [Fact]
    public async Task UserCheckFailsWhenAnotherRequestWroteTheUserRow()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveConfirmedAccountAsync(databaseContext, "test-sacs-controle@exemple.test");
        var holidays = new Bag { Name = "Vacances", UserId = account.Id };
        await SaveBagsAsync(databaseContext, holidays);
        var repository = new BagRepository(databaseContext);
        var user = await repository.FindUserAsync(account.Id);

        await WriteAsAnotherRequestAsync(databaseContext, async () =>
            await databaseContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE utilisateur SET nb_tentatives_echouees = 1 WHERE id_utilisateur = {account.Id}"));
        repository.PrepareMovement(new BagMovement { AmountInCents = 1000, MovedAt = Now, DestinationBagId = holidays.Id });
        repository.PrepareUserCheck(user!);

        Assert.False(await TrySaveChangesAsync(databaseContext));
        Assert.False(await databaseContext.BagMovements.AnyAsync(row => row.DestinationBagId == holidays.Id));
    }

    // Sans autre requête, le contrôle passe, et il réécrit la ligne avec ses propres valeurs.
    [Fact]
    public async Task UserCheckPassesAndChangesNothingInTheUserRow()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveConfirmedAccountAsync(databaseContext, "test-sacs-controle-ok@exemple.test");
        var holidays = new Bag { Name = "Vacances", UserId = account.Id };
        await SaveBagsAsync(databaseContext, holidays);
        var repository = new BagRepository(databaseContext);
        var user = await repository.FindUserAsync(account.Id);

        repository.PrepareMovement(new BagMovement { AmountInCents = 1000, MovedAt = Now, DestinationBagId = holidays.Id });
        repository.PrepareUserCheck(user!);

        Assert.True(await TrySaveChangesAsync(databaseContext));
        databaseContext.ChangeTracker.Clear();
        var savedUser = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Id == account.Id);
        Assert.Equal(0, savedUser.FailedLoginAttemptCount);
        Assert.True(await databaseContext.BagMovements.AnyAsync(row => row.DestinationBagId == holidays.Id));
    }

    // --- Requêtes simultanées

    // Une autre requête supprime le sac visé juste avant l'enregistrement du mouvement : la clé étrangère le refuse,
    // et c'est un conflit à rejouer, pas une panne. C'est vrai pour le sac d'arrivée comme pour le sac de départ.
    [Fact]
    public async Task MovementWithABagDeletedByAnotherRequestIsAConflict()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveConfirmedAccountAsync(databaseContext, "test-sacs-conflit@exemple.test");
        var holidays = new Bag { Name = "Vacances", UserId = account.Id };
        var savings = new Bag { Name = "Épargne", UserId = account.Id };
        await SaveBagsAsync(databaseContext, holidays, savings);
        await SaveMovementAsync(databaseContext, null, savings, 5000);
        var repository = new BagRepository(databaseContext);

        await WriteAsAnotherRequestAsync(databaseContext, async () =>
            await databaseContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM sac WHERE id_sac = {holidays.Id}"));
        repository.PrepareMovement(new BagMovement { AmountInCents = 1000, MovedAt = Now, SourceBagId = savings.Id, DestinationBagId = holidays.Id });
        var intoDeletedBag = await TrySaveChangesAsync(databaseContext);
        repository.PrepareMovement(new BagMovement { AmountInCents = 1000, MovedAt = Now, SourceBagId = holidays.Id });
        var fromDeletedBag = await TrySaveChangesAsync(databaseContext);

        Assert.False(intoDeletedBag);
        Assert.False(fromDeletedBag);
        Assert.Empty(databaseContext.ChangeTracker.Entries());
        Assert.Equal(5000, (await repository.SumBalancesAsync(account.Id))[savings.Id]);
        Assert.Equal(1, await databaseContext.BagMovements.CountAsync(row => row.DestinationBagId == savings.Id));
    }

    // La suppression par le dépôt laisse la base appliquer ses règles : le mouvement avec l'argent disponible disparaît,
    // celui avec l'autre sac garde l'autre sac, dont le solde ne change pas.
    [Fact]
    public async Task DeletionLetsTheDatabaseApplyItsRules()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveConfirmedAccountAsync(databaseContext, "test-sacs-suppression-depot@exemple.test");
        var holidays = new Bag { Name = "Vacances", UserId = account.Id };
        var savings = new Bag { Name = "Épargne", UserId = account.Id };
        await SaveBagsAsync(databaseContext, holidays, savings);
        await SaveMovementAsync(databaseContext, null, holidays, 50_000);
        var transfer = await SaveMovementAsync(databaseContext, holidays, savings, 20_000);
        var repository = new BagRepository(databaseContext);

        repository.PrepareDeletion((await repository.FindAsync(account.Id, holidays.Id))!);

        Assert.True(await TrySaveChangesAsync(databaseContext));
        databaseContext.ChangeTracker.Clear();
        var balances = await repository.SumBalancesAsync(account.Id);
        Assert.Equal(new List<Guid> { savings.Id }, balances.Keys.ToList());
        Assert.Equal(20_000, balances[savings.Id]);
        var keptTransfer = await databaseContext.BagMovements.AsNoTracking().SingleAsync(row => row.Id == transfer.Id);
        Assert.Null(keptTransfer.SourceBagId);
        Assert.Equal(1, await databaseContext.BagMovements.CountAsync(row => row.SourceBagId == null && row.DestinationBagId == savings.Id));
    }

    // Vraie course, avec des lignes enregistrées pour de vrai : deux sacs reliés par un mouvement sont supprimés au même instant.
    // L'autre transaction supprime le premier sac et vide son côté du mouvement, sans valider. La suppression du second sac
    // veut vider l'autre côté du même mouvement, et attend. Quand l'autre transaction valide, le mouvement n'aurait plus aucun sac :
    // la base refuse (ck_mouvement_un_sac), et l'unité de travail y voit un conflit à rejouer. Le passage suivant réussit,
    // car le déclencheur voit maintenant un mouvement avec l'argent disponible, et l'efface.
    [Fact]
    public async Task TwoLinkedBagsDeletedAtTheSameMomentAreBothDeleted()
    {
        var email = TestAccountCleanup.CreateUniqueEmail("test-sacs-suppressions-simultanees");

        try
        {
            await using var databaseContext = CreateDatabaseContext();
            var account = await SaveConfirmedAccountAsync(databaseContext, email);
            var holidays = new Bag { Name = "Vacances", UserId = account.Id };
            var savings = new Bag { Name = "Épargne", UserId = account.Id };
            await SaveBagsAsync(databaseContext, holidays, savings);
            await SaveMovementAsync(databaseContext, null, holidays, 50_000);
            var transfer = await SaveMovementAsync(databaseContext, holidays, savings, 20_000);
            var repository = new BagRepository(databaseContext);
            repository.PrepareDeletion((await repository.FindAsync(account.Id, savings.Id))!);

            await databaseContext.Database.OpenConnectionAsync();
            var savingProcessId = ((NpgsqlConnection)databaseContext.Database.GetDbConnection()).ProcessID;
            await using var otherConnection = new NpgsqlConnection(ReadConnectionString());
            await otherConnection.OpenAsync();
            await using var otherTransaction = await otherConnection.BeginTransactionAsync();
            await using (var otherDeletion = new NpgsqlCommand("DELETE FROM sac WHERE id_sac = @id", otherConnection, otherTransaction))
            {
                otherDeletion.Parameters.AddWithValue("id", holidays.Id);
                await otherDeletion.ExecuteNonQueryAsync();
            }

            var saving = TrySaveChangesAsync(databaseContext);
            await WaitUntilBlockedAsync(otherTransaction, savingProcessId);
            await otherTransaction.CommitAsync();

            Assert.False(await saving.WaitAsync(MaximumWait));
            Assert.True(await databaseContext.Bags.AnyAsync(row => row.Id == savings.Id));
            var emptiedTransfer = await databaseContext.BagMovements.AsNoTracking().SingleAsync(row => row.Id == transfer.Id);
            Assert.Null(emptiedTransfer.SourceBagId);
            Assert.Equal(savings.Id, emptiedTransfer.DestinationBagId);

            var secondTry = await new BagDeletionService(repository, CreateSavingService(databaseContext)).DeleteAsync(account.Id, savings.Id);

            Assert.IsType<BagDeletionResult.Deleted>(secondTry);
            Assert.False(await databaseContext.Bags.AnyAsync(row => row.UserId == account.Id));
            Assert.False(await databaseContext.BagMovements.AnyAsync(row => row.Id == transfer.Id));
        }
        finally
        {
            await TestAccountCleanup.DeleteAccountAsync(email);
        }
    }

    // Attend que PostgreSQL montre l'enregistrement bloqué par l'autre transaction.
    private static async Task WaitUntilBlockedAsync(NpgsqlTransaction otherTransaction, int savingProcessId)
    {
        var deadline = DateTime.UtcNow + MaximumWait;

        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand("SELECT @other = ANY(pg_blocking_pids(@saving))", otherTransaction.Connection, otherTransaction);
            command.Parameters.AddWithValue("other", otherTransaction.Connection!.ProcessID);
            command.Parameters.AddWithValue("saving", savingProcessId);

            if ((bool)(await command.ExecuteScalarAsync())!)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("L'enregistrement n'a jamais attendu l'autre transaction.");
    }
}
