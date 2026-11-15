using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Application.Bags;
using WalletFlow.Application.Notifications;
using WalletFlow.Application.Transactions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Courses entre requêtes simultanées sur les alertes, avec la vraie API et la vraie base de test (DD §9.3) : chaque requête
// a son propre accès à la base et sa propre transaction, comme dans l'API. Une alerte ne part qu'une fois, même quand
// vingt requêtes la déclenchent au même instant.
[Collection(RealDatabaseCollection.Name)]
public class ConcurrentAlertRequestsTests
{
    private const int SimultaneousRequestCount = 20;

    // Enregistre pour de vrai un compte confirmé avec ses 8 catégories, une connexion et un compte bancaire de 100,00 €.
    private static async Task<(User Account, BankAccount BankAccount)> SaveAccountAsync(string email)
    {
        var account = new User { Email = email, PasswordHash = "empreinte-de-test", IsEmailConfirmed = true };
        var connection = new BankConnection
        {
            ExternalConnectionId = "session-" + Guid.NewGuid(),
            BankName = "Banque de test",
            AuthorizationExpiresAt = DateTime.UtcNow.AddDays(100),
            UserId = account.Id
        };
        var bankAccount = new BankAccount
        {
            ExternalAccountId = "compte-" + Guid.NewGuid(),
            Name = "Compte courant",
            BalanceInCents = 10_000,
            BalanceUpdatedAt = DateTime.UtcNow,
            BankConnectionId = connection.Id
        };

        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.Users.Add(account);
        databaseContext.Categories.AddRange(DefaultCategories.CreateForUser(account.Id));
        databaseContext.AddRange(connection, bankAccount);
        await databaseContext.SaveChangesAsync();

        return (account, bankAccount);
    }

    // Attend que la tâche de fond ait vidé la file de ce compte, 10 secondes au plus.
    private static async Task WaitUntilQueueIsEmptyAsync(WalletFlowDatabaseContext databaseContext, Guid accountId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (await databaseContext.EmailRequests.AnyAsync(row => row.UserId == accountId) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.False(await databaseContext.EmailRequests.AnyAsync(row => row.UserId == accountId));
    }

    // 20 opérations de 5,00 € passent en même temps dans « Loisirs », dont le budget est de 100,00 € : chaque vérification
    // relit les dépenses après son propre enregistrement. À la fin, les deux seuils sont notés une seule fois chacun,
    // l'e-mail des 100 % part une fois, et celui des 80 % au plus une fois, selon que ce seuil a été vu seul ou non.
    [Fact]
    public async Task SimultaneousSpendingRecordsEachThresholdOnce()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-budget");

        try
        {
            var (account, bankAccount) = await SaveAccountAsync(email);
            var today = UsersCalendar.FindToday(DateTimeOffset.UtcNow);
            List<BankTransaction> operations;
            Guid leisureId;

            await using (var setupContext = LocalTestDatabase.CreateDatabaseContext())
            {
                var leisure = await setupContext.Categories.SingleAsync(row => row.UserId == account.Id && row.Name == "Loisirs");
                leisure.ReplaceMonthlyBudget(10_000);
                leisureId = leisure.Id;
                var foodId = await setupContext.Categories.Where(row => row.UserId == account.Id && row.Name == "Alimentation")
                    .Select(row => row.Id).SingleAsync();
                operations = Enumerable.Range(0, SimultaneousRequestCount).Select(number => new BankTransaction
                {
                    ExternalTransactionId = "course-budget-" + number,
                    TransactionDate = today,
                    Label = "OPERATION DE COURSE " + number,
                    AmountInCents = -500,
                    BankAccountId = bankAccount.Id,
                    CategoryId = foodId
                }).ToList();
                setupContext.BankTransactions.AddRange(operations);
                await setupContext.SaveChangesAsync();
            }

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, requestNumber) =>
                services.GetRequiredService<TransactionCategoryChangeService>()
                    .ChangeAsync(account.Id, operations[requestNumber].Id, leisureId, false));

            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            Assert.All(results, result => Assert.IsType<TransactionCategoryChangeResult.Changed>(result));
            var thresholds = await databaseContext.Alerts.AsNoTracking()
                .Where(row => row.UserId == account.Id)
                .Select(row => row.Threshold)
                .ToListAsync();
            Assert.Equal(new List<short?> { 80, 100 }, thresholds.OrderBy(threshold => threshold).ToList());
            await WaitUntilQueueIsEmptyAsync(databaseContext, account.Id);
            var subjects = api.EmailSender.SentEmails.Where(sent => sent.RecipientAddress == email).Select(sent => sent.Subject).ToList();
            Assert.Equal(1, subjects.Count(subject => subject == "Alerte de budget : budget atteint"));
            Assert.InRange(subjects.Count(subject => subject == "Alerte de budget : 80 % atteints"), 0, 1);
            Assert.Equal(subjects.Count, subjects.Count(subject => subject.StartsWith("Alerte de budget")));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // 20 déplacements de 5,00 € en même temps vers un sac dont l'objectif est de 5,00 € : le premier enregistré l'atteint,
    // les autres passent ensuite un par un, et une seule alerte part.
    [Fact]
    public async Task SimultaneousMovementsSendOneGoalAlert()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-objectif");

        try
        {
            var (account, _) = await SaveAccountAsync(email);
            var bag = new Bag { Name = "Vacances", UserId = account.Id };
            bag.ReplaceGoal(500, null, UsersCalendar.FindToday(DateTimeOffset.UtcNow));

            await using (var setupContext = LocalTestDatabase.CreateDatabaseContext())
            {
                setupContext.Bags.Add(bag);
                await setupContext.SaveChangesAsync();
            }

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, requestNumber) =>
                services.GetRequiredService<BagMovementService>().MoveAsync(account.Id, null, bag.Id, 500));

            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            Assert.Contains(results, result => result is BagMovementResult.Moved);
            var alert = await databaseContext.Alerts.AsNoTracking().SingleAsync(row => row.UserId == account.Id);
            Assert.Equal((AlertKind.BagGoalReached, (Guid?)bag.Id), (alert.Kind, alert.BagId));
            await WaitUntilQueueIsEmptyAsync(databaseContext, account.Id);
            Assert.Equal(1, api.EmailSender.CountSentEmailsTo(email));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // 20 premiers choix en même temps pour le même type d'alerte : la clé de parametrer n'en garde qu'un, et les autres
    // le relisent puis le changent. Aucun choix ne se perd en panne.
    [Fact]
    public async Task SimultaneousFirstChoicesKeepOneRow()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-reglages");

        try
        {
            var (account, _) = await SaveAccountAsync(email);

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, requestNumber) =>
                services.GetRequiredService<AlertSettingsService>().SwitchAsync(account.Id, AlertKind.NewSuggestion, false));

            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            Assert.All(results, result => Assert.IsType<AlertSettingsResult.Saved>(result));
            var preference = await databaseContext.AlertPreferences.AsNoTracking().SingleAsync(row => row.UserId == account.Id);
            Assert.Equal((AlertKind.NewSuggestion, false), (preference.Kind, preference.IsEnabled));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }
}
