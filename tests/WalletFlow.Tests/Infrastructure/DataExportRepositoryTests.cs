using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using WalletFlow.Application.DataExport;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.BankTestRows;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la lecture des données d'un compte pour son fichier (GR Compte, SS §6), dans la vraie base : toutes ses données, rien
// d'un autre compte, aucun secret ; et une lecture faite au même instant de la base, sans aucune écriture possible.
public class DataExportRepositoryTests
{
    private const string AddedCategoryName = "Ajoutée pendant la lecture";

    // Le fichier d'un compte contient chacune de ses lignes, dans un ordre fixe, et rien d'un autre compte : ni ses lignes, ni la
    // demande qu'un visiteur a envoyée avec la même adresse. Aucun secret n'y entre : ni l'empreinte du mot de passe, ni les
    // identifiants chez le prestataire bancaire.
    [Fact]
    public async Task FileHoldsEveryRowOfTheAccountAndNothingElse()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var owner = await FullAccountRows.SaveAsync(databaseContext, "test-export@exemple.test", "stable-export-proprietaire");
        var other = await FullAccountRows.SaveAsync(databaseContext, "test-export-autre@exemple.test", "stable-export-autre");
        databaseContext.SupportRequests.Add(SupportRequest.FromVisitor("test-export@exemple.test", "Je suis un visiteur.", Now));
        await databaseContext.SaveChangesAsync();

        var data = await new DataExportRepository(databaseContext).ReadAsync(owner.Account.Id);

        Assert.NotNull(data);
        Assert.Equal(new ExportedProfile("test-export@exemple.test", "Durand", "Alice", false, NotificationChannel.EmailAndPhone), data.Profile);
        Assert.Equal(
            new ExportedBankConnection(owner.Connection.Id, "Banque de test", BankConnectionStatus.Active, owner.Connection.AuthorizationExpiresAt),
            Assert.Single(data.BankConnections));
        Assert.Equal(new ExportedBankAccount(owner.BankAccount.Id, owner.Connection.Id, "Compte courant", 10_000, Now), Assert.Single(data.BankAccounts));
        Assert.Equal(
            owner.Transactions
                .OrderBy(operation => operation.TransactionDate)
                .Select(operation => new ExportedTransaction(
                    operation.Id,
                    operation.BankAccountId,
                    operation.TransactionDate,
                    operation.Label,
                    operation.AmountInCents,
                    operation.CategoryId,
                    operation.IsInternalTransfer))
                .ToList(),
            data.Transactions);
        Assert.Equal(owner.Categories.Select(category => category.Id).Order().ToList(), data.Categories.Select(category => category.Id).Order().ToList());
        Assert.Equal(data.Categories.OrderBy(category => category.Name, StringComparer.Ordinal).ToList(), data.Categories);
        Assert.Equal(20_000, data.Categories.Single(category => category.Id == owner.Leisure.Id).MonthlyBudgetInCents);
        Assert.Equal(new ExportedCategorizationRule("CB CARREFOUR", owner.Leisure.Id), Assert.Single(data.CategorizationRules));
        Assert.Equal(
            new List<ExportedBag>
            {
                new ExportedBag(owner.Bags[0].Id, "Vacances", 100_000, new DateOnly(2027, 7, 1)),
                new ExportedBag(owner.Bags[1].Id, "Voiture", null, null)
            },
            data.Bags);
        Assert.Equal(
            owner.Movements
                .Select(movement => new ExportedBagMovement(
                    movement.Id,
                    movement.MovedAt,
                    movement.AmountInCents,
                    movement.SourceBagId,
                    movement.DestinationBagId,
                    movement.SavingsSuggestionId))
                .ToList(),
            data.BagMovements);
        Assert.Equal(
            new ExportedSuggestion(owner.Suggestion.Id, new DateOnly(2026, 10, 1), SuggestionRule.UnallocatedMoney, null, 2500, SuggestionStatus.Accepted, Now.AddDays(-2)),
            Assert.Single(data.Suggestions));
        Assert.Equal(10, data.AlertSettings.Count);
        Assert.Equal(new List<AlertKind> { AlertKind.BudgetReached }, data.AlertSettings.Where(setting => !setting.IsEnabled).Select(setting => setting.Kind).ToList());
        Assert.Equal(data.AlertSettings.OrderBy(setting => setting.Kind).ToList(), data.AlertSettings);
        Assert.Equal(
            new ExportedAlert(owner.Alert.Id, AlertKind.BagGoalReached, Now, null, null, null, owner.Bags[0].Id, null),
            Assert.Single(data.Alerts));
        var request = Assert.Single(data.SupportRequests);
        Assert.Equal((owner.Request.Id, "Où est mon sac ?", SupportRequestStatus.Sent), (request.Id, request.Message, request.Status));
        Assert.Equal(new ExportedSupportResponse(Now.AddDays(-2), "Dans la liste des sacs."), Assert.Single(request.Responses));

        var file = JsonSerializer.Serialize(data);
        var forbiddenTexts = new List<string>
        {
            other.Account.Email, other.Account.Id.ToString(), other.Connection.Id.ToString(), other.BankAccount.Id.ToString(),
            other.Bags[0].Id.ToString(), other.Movements[0].Id.ToString(), other.Suggestion.Id.ToString(), other.Alert.Id.ToString(),
            other.Request.Id.ToString(), other.Transactions[0].Id.ToString(), other.Leisure.Id.ToString(), "Je suis un visiteur.",
            "empreinte-de-test", owner.Connection.ExternalConnectionId, "stable-export-proprietaire", owner.Account.Id.ToString()
        };
        Assert.All(forbiddenTexts, forbidden => Assert.DoesNotContain(forbidden, file));
    }

    // Un compte qui n'existe plus n'a pas de fichier.
    [Fact]
    public async Task UnknownAccountHasNoFile()
    {
        await using var databaseContext = CreateDatabaseContext();

        Assert.Null(await new DataExportRepository(databaseContext).ReadAsync(Guid.NewGuid()));
    }

    // La lecture voit la base telle qu'elle était à sa première commande : une catégorie qu'une autre requête enregistre entre
    // deux lectures n'apparaît pas. Et elle ne peut rien écrire : PostgreSQL refuse toute écriture dans sa transaction (25006).
    [Fact]
    public async Task ReadingSeesTheBaseAtOneInstantAndCannotWrite()
    {
        User account;
        await using (var setupContext = CreateDatabaseContext())
        {
            account = await SaveAccountAsync(setupContext, "test-export-instant-" + Guid.NewGuid().ToString("N") + "@exemple.test");
        }

        try
        {
            var interceptor = new ActBeforeTheSecondRead(account.Id);
            await using var databaseContext = CreateDatabaseContext(interceptor);

            var data = await new DataExportRepository(databaseContext).ReadAsync(account.Id);

            Assert.True(interceptor.HasActed);
            Assert.Equal(8, data!.Categories.Count);
            Assert.DoesNotContain(data.Categories, category => category.Name == AddedCategoryName);
            Assert.Equal(PostgresErrorCodes.ReadOnlySqlTransaction, interceptor.RefusedWriteState);
            await using var checkContext = CreateDatabaseContext();
            Assert.Equal(9, await checkContext.Categories.CountAsync(category => category.UserId == account.Id));
        }
        finally
        {
            await using var cleanupContext = CreateDatabaseContext();
            await cleanupContext.Users.Where(user => user.Id == account.Id).ExecuteDeleteAsync();
        }
    }

    // Agit juste avant la deuxième lecture : la première a déjà fixé l'instant que voit la transaction. Une autre requête y
    // enregistre une catégorie du compte, puis la lecture elle-même essaie d'écrire, dans un point de sauvegarde annulé ensuite.
    private sealed class ActBeforeTheSecondRead : DbCommandInterceptor
    {
        private readonly Guid accountId;
        private int readCount = 0;

        public ActBeforeTheSecondRead(Guid accountId)
        {
            this.accountId = accountId;
        }

        public bool HasActed { get; private set; } = false;
        public string? RefusedWriteState { get; private set; } = null;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            readCount = readCount + 1;

            if (readCount == 2)
            {
                await using (var otherRequest = CreateDatabaseContext())
                {
                    otherRequest.Categories.Add(new Category { Name = AddedCategoryName, UserId = accountId });
                    await otherRequest.SaveChangesAsync(cancellationToken);
                }

                RefusedWriteState = await TryWriteAsync(command, cancellationToken);
                HasActed = true;
            }

            return result;
        }

        // Essaie d'écrire dans la transaction de la lecture. Le point de sauvegarde garde la transaction utilisable après le refus.
        private async Task<string?> TryWriteAsync(DbCommand readCommand, CancellationToken cancellationToken)
        {
            await ExecuteAsync(readCommand, "SAVEPOINT essai_d_ecriture", cancellationToken);

            try
            {
                await ExecuteAsync(
                    readCommand,
                    "UPDATE utilisateur SET nom_utilisateur = nom_utilisateur WHERE id_utilisateur = '" + accountId + "'",
                    cancellationToken);
                return null;
            }
            catch (PostgresException exception)
            {
                return exception.SqlState;
            }
            finally
            {
                await ExecuteAsync(readCommand, "ROLLBACK TO SAVEPOINT essai_d_ecriture", cancellationToken);
            }
        }

        private static async Task ExecuteAsync(DbCommand readCommand, string sql, CancellationToken cancellationToken)
        {
            await using var command = readCommand.Connection!.CreateCommand();
            command.Transaction = readCommand.Transaction;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
