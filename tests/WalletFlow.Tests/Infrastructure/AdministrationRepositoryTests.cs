using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Administration;
using WalletFlow.Application.Support;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Tests.EndToEnd;
using static WalletFlow.Tests.Infrastructure.BankTestRows;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie l'accès de l'administrateur sur la vraie base (GR Admin, DD §10 et §11) : la liste par état, la lecture d'une demande,
// une réponse enregistrée avec la version de sa demande, et les statistiques. Chaque test annule sa transaction à la fin.
// Les statistiques comptent toute la base : ces tests passent dans la collection des tests qui écrivent pour de vrai, un par un,
// et comparent l'avant et l'après dans leur propre transaction.
[Collection(RealDatabaseCollection.Name)]
public class AdministrationRepositoryTests
{
    // Enregistre ces demandes et ces réponses, puis oublie les objets gardés en mémoire.
    private static async Task SaveAsync(WalletFlowDatabaseContext databaseContext, IEnumerable<SupportRequest> requests, params SupportResponse[] responses)
    {
        databaseContext.SupportRequests.AddRange(requests);
        databaseContext.SupportResponses.AddRange(responses);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();
    }

    // Lit la version de la ligne d'une demande (xmin) : elle change à chaque écriture de la ligne.
    private static async Task<string> ReadRowVersionAsync(WalletFlowDatabaseContext databaseContext, Guid requestId)
    {
        return await databaseContext.Database
            .SqlQuery<string>($"SELECT xmin::text AS \"Value\" FROM demande_support WHERE id_demande = {requestId}")
            .SingleAsync();
    }

    // La liste d'un état vient la plus ancienne d'abord, avec l'adresse du compte, ou celle du visiteur, et le nombre de réponses.
    [Fact]
    public async Task ListOfAStatusIsOldestFirstWithReplyAddressAndResponses()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountAsync(databaseContext, "test-admin-liste@exemple.test");
        var fromAccount = new SupportRequest { Message = "Compte", SentAt = Now.AddDays(-2), UserId = account.Id };
        var fromVisitor = new SupportRequest { Message = "Visiteur", ContactEmail = "test-admin-visiteur@exemple.test", SentAt = Now.AddDays(-3) };
        var resolved = new SupportRequest { Message = "Résolue", Status = SupportRequestStatus.Resolved, SentAt = Now.AddDays(-4), UserId = account.Id };
        await SaveAsync(databaseContext, new[] { fromAccount, fromVisitor, resolved },
            new SupportResponse { Content = "Première", SentAt = Now, SupportRequestId = fromAccount.Id },
            new SupportResponse { Content = "Seconde", SentAt = Now, SupportRequestId = fromAccount.Id });
        var testIds = new[] { fromAccount.Id, fromVisitor.Id, resolved.Id };

        var list = (await new AdministrationRepository(databaseContext).FindRequestsAsync(SupportRequestStatus.Sent))
            .Where(item => testIds.Contains(item.Id))
            .ToList();

        Assert.Equal(
            new List<AdminSupportRequestItem>
            {
                new AdminSupportRequestItem(fromVisitor.Id, fromVisitor.SentAt, SupportRequestStatus.Sent, "test-admin-visiteur@exemple.test", null, 0),
                new AdminSupportRequestItem(fromAccount.Id, fromAccount.SentAt, SupportRequestStatus.Sent, "test-admin-liste@exemple.test", account.Id, 2)
            },
            list);
    }

    // Une demande se lit avec son message, l'adresse où répondre, et ses réponses de la plus ancienne à la plus récente.
    [Fact]
    public async Task DetailsGiveTheMessageReplyAddressAndResponsesInOrder()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountAsync(databaseContext, "test-admin-lecture@exemple.test");
        var request = new SupportRequest { Message = "J'ai perdu mon téléphone.", SentAt = Now.AddDays(-1), UserId = account.Id };
        await SaveAsync(databaseContext, new[] { request },
            new SupportResponse { Content = "Seconde", SentAt = Now, SupportRequestId = request.Id },
            new SupportResponse { Content = "Première", SentAt = Now.AddHours(-1), SupportRequestId = request.Id });
        var repository = new AdministrationRepository(databaseContext);

        var details = await repository.FindRequestDetailsAsync(request.Id);

        Assert.NotNull(details);
        Assert.Equal(("J'ai perdu mon téléphone.", "test-admin-lecture@exemple.test", (Guid?)account.Id), (details.Message, details.ReplyAddress, details.AccountId));
        Assert.Equal(new List<string> { "Première", "Seconde" }, details.Responses.Select(response => response.Content).ToList());
        Assert.Null(await repository.FindRequestDetailsAsync(Guid.NewGuid()));
    }

    // Une réponse est enregistrée avec la demande « en cours ». La ligne de la demande est réécrite même si son état ne change pas :
    // sa version change, et une écriture simultanée de la même demande échouerait.
    [Fact]
    public async Task AnswerRewritesTheRequestRowEvenWithoutStatusChange()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var request = new SupportRequest
        {
            Message = "Bonjour",
            ContactEmail = "test-admin-reponse@exemple.test",
            Status = SupportRequestStatus.InProgress,
            SentAt = Now.AddDays(-1)
        };
        await SaveAsync(databaseContext, new[] { request });
        var versionBefore = await ReadRowVersionAsync(databaseContext, request.Id);
        var repository = new AdministrationRepository(databaseContext);

        var tracked = await repository.FindRequestAsync(request.Id);
        Assert.NotNull(tracked);
        var response = SupportResponse.Create(tracked.Id, "Réponse", Now);
        tracked.NoteResponse();
        repository.PrepareRequestCheck(tracked);
        repository.PrepareResponse(response);

        Assert.True(await TrySaveChangesAsync(databaseContext));
        databaseContext.ChangeTracker.Clear();
        Assert.NotEqual(versionBefore, await ReadRowVersionAsync(databaseContext, request.Id));
        Assert.Equal(SupportRequestStatus.InProgress, (await databaseContext.SupportRequests.SingleAsync(row => row.Id == request.Id)).Status);
        Assert.True(await databaseContext.SupportResponses.AnyAsync(row => row.Id == response.Id));
    }

    // Une réponse à une demande effacée au même instant, par la tâche qui efface les demandes d'un an, est refusée par la base :
    // c'est un conflit à rejouer, et le passage suivant ne trouve plus la demande.
    [Fact]
    public async Task AnswerToARequestDeletedMeanwhileIsAConflict()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        new AdministrationRepository(databaseContext).PrepareResponse(SupportResponse.Create(Guid.NewGuid(), "Réponse", Now));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => databaseContext.SaveChangesAsync());

        Assert.True(PostgresErrors.IsConflict(exception));
    }

    // Les statistiques comptent les comptes par état comme la règle du domaine, les connexions actives et les demandes par état.
    // Une demande de suppression passe avant tout : un compte bloqué ou non confirmé qui part est compté « en suppression ».
    [Fact]
    public async Task StatisticsCountEachStateLikeTheDomainRule()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new AdministrationRepository(databaseContext);
        var before = await repository.CountStatisticsAsync(Now);
        var active = new User { Email = "test-stat-actif@exemple.test", PasswordHash = "empreinte", IsEmailConfirmed = true };
        databaseContext.Users.AddRange(
            active,
            new User { Email = "test-stat-non-confirme@exemple.test", PasswordHash = "empreinte" },
            new User { Email = "test-stat-bloque@exemple.test", PasswordHash = "empreinte", IsEmailConfirmed = true, LockoutEndsAt = Now.AddMinutes(10) },
            new User { Email = "test-stat-fin-blocage@exemple.test", PasswordHash = "empreinte", IsEmailConfirmed = true, LockoutEndsAt = Now },
            new User { Email = "test-stat-suppression@exemple.test", PasswordHash = "empreinte", IsEmailConfirmed = true, DeletionRequestedAt = Now.AddDays(-1) },
            new User
            {
                Email = "test-stat-bloque-suppression@exemple.test",
                PasswordHash = "empreinte",
                IsEmailConfirmed = true,
                LockoutEndsAt = Now.AddMinutes(10),
                DeletionRequestedAt = Now.AddDays(-1)
            },
            new User { Email = "test-stat-non-confirme-suppression@exemple.test", PasswordHash = "empreinte", DeletionRequestedAt = Now.AddDays(-1) });
        databaseContext.BankConnections.AddRange(
            new BankConnection { ExternalConnectionId = "stat-" + Guid.NewGuid(), BankName = "Banque", AuthorizationExpiresAt = Now.AddDays(90), UserId = active.Id },
            new BankConnection
            {
                ExternalConnectionId = "stat-" + Guid.NewGuid(),
                BankName = "Banque",
                AuthorizationExpiresAt = Now.AddDays(-1),
                Status = BankConnectionStatus.Expired,
                UserId = active.Id
            });
        await SaveAsync(databaseContext, new[]
        {
            new SupportRequest { Message = "a", SentAt = Now, UserId = active.Id },
            new SupportRequest { Message = "b", ContactEmail = "test-stat-visiteur@exemple.test", SentAt = Now },
            new SupportRequest { Message = "c", Status = SupportRequestStatus.InProgress, SentAt = Now, UserId = active.Id },
            new SupportRequest { Message = "d", Status = SupportRequestStatus.Resolved, SentAt = Now, UserId = active.Id }
        });

        var after = await repository.CountStatisticsAsync(Now);

        Assert.Equal(
            before with
            {
                NotConfirmedAccounts = before.NotConfirmedAccounts + 1,
                ActiveAccounts = before.ActiveAccounts + 2,
                LockedAccounts = before.LockedAccounts + 1,
                AccountsBeingDeleted = before.AccountsBeingDeleted + 3,
                ActiveBankConnections = before.ActiveBankConnections + 1,
                SentRequests = before.SentRequests + 2,
                RequestsInProgress = before.RequestsInProgress + 1,
                ResolvedRequests = before.ResolvedRequests + 1
            },
            after);
    }
}
