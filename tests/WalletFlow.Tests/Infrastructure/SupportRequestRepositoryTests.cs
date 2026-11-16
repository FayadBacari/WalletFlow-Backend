using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Support;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.BankTestRows;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les demandes de support sur la vraie base (DD §10, MPD) : les dates gardées, le compte des demandes récentes, l'ordre
// de la liste et des réponses, l'appartenance, et l'effacement en cascade des réponses et de leurs e-mails.
// Chaque test annule sa transaction à la fin : la base reste comme avant.
public class SupportRequestRepositoryTests
{
    // Enregistre ces demandes et ces réponses, puis oublie les objets gardés en mémoire.
    private static async Task SaveAsync(WalletFlowDatabaseContext databaseContext, IEnumerable<SupportRequest> requests, params SupportResponse[] responses)
    {
        databaseContext.SupportRequests.AddRange(requests);
        databaseContext.SupportResponses.AddRange(responses);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();
    }

    // Une demande enregistrée par le dépôt garde sa date et son message, et ne se relit qu'avec son utilisateur.
    [Fact]
    public async Task RequestIsSavedWithItsDateAndReadOnlyByItsUser()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountAsync(databaseContext, "test-support-lecture@exemple.test");
        var otherAccount = await SaveAccountAsync(databaseContext, "test-support-lecture-autre@exemple.test");
        var repository = new SupportRequestRepository(databaseContext);
        var request = SupportRequest.FromUser(account.Id, "Bonjour,\nmon sac a disparu.", Now);
        var visitorRequest = SupportRequest.FromVisitor("visiteur@exemple.test", "Bonjour", Now);
        repository.PrepareRequest(request);
        repository.PrepareRequest(visitorRequest);
        Assert.True(await TrySaveChangesAsync(databaseContext));
        databaseContext.ChangeTracker.Clear();

        var details = await repository.FindUserRequestAsync(account.Id, request.Id);

        Assert.NotNull(details);
        Assert.Equal(
            (request.Id, Now, SupportRequestStatus.Sent, "Bonjour,\nmon sac a disparu."),
            (details.Id, details.SentAt, details.Status, details.Message));
        Assert.Empty(details.Responses);
        Assert.Null(await repository.FindUserRequestAsync(otherAccount.Id, request.Id));
        Assert.Null(await repository.FindUserRequestAsync(account.Id, visitorRequest.Id));
        Assert.Null(await repository.FindUserRequestAsync(account.Id, Guid.NewGuid()));
        Assert.Equal("visiteur@exemple.test", (await databaseContext.SupportRequests.SingleAsync(row => row.Id == visitorRequest.Id)).ContactEmail);
    }

    // Le compte des demandes récentes prend le moment limite lui-même, et seulement les demandes de l'utilisateur.
    [Fact]
    public async Task CountTakesTheLimitMomentAndOnlyTheUsersRequests()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountAsync(databaseContext, "test-support-compte@exemple.test");
        var otherAccount = await SaveAccountAsync(databaseContext, "test-support-compte-autre@exemple.test");
        var limit = Now - SupportRules.RequestDay;
        await SaveAsync(databaseContext, new[]
        {
            SupportRequest.FromUser(account.Id, "À la limite", limit),
            SupportRequest.FromUser(account.Id, "Juste avant", limit - TimeSpan.FromMicroseconds(1)),
            SupportRequest.FromUser(account.Id, "Récente", Now),
            SupportRequest.FromUser(otherAccount.Id, "D'un autre", Now)
        });
        var repository = new SupportRequestRepository(databaseContext);

        Assert.Equal(2, await repository.CountSentSinceAsync(account.Id, limit));
        Assert.Equal(1, await repository.CountSentSinceAsync(account.Id, limit + TimeSpan.FromMicroseconds(1)));
    }

    // La liste vient la plus récente d'abord, avec le nombre de réponses de chacune ; les réponses d'une demande, de la plus
    // ancienne à la plus récente.
    [Fact]
    public async Task ListIsNewestFirstAndResponsesOldestFirst()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountAsync(databaseContext, "test-support-liste@exemple.test");
        var older = SupportRequest.FromUser(account.Id, "Ancienne", Now.AddDays(-2));
        var newer = SupportRequest.FromUser(account.Id, "Récente", Now.AddDays(-1));
        await SaveAsync(databaseContext, new[] { older, newer },
            new SupportResponse { Content = "Seconde", SentAt = Now, SupportRequestId = older.Id },
            new SupportResponse { Content = "Première", SentAt = Now.AddHours(-1), SupportRequestId = older.Id });
        var repository = new SupportRequestRepository(databaseContext);

        var list = await repository.FindUserRequestsAsync(account.Id);
        var details = await repository.FindUserRequestAsync(account.Id, older.Id);

        Assert.Equal(
            new List<SupportRequestSummary>
            {
                new SupportRequestSummary(newer.Id, newer.SentAt, SupportRequestStatus.Sent, 0),
                new SupportRequestSummary(older.Id, older.SentAt, SupportRequestStatus.Sent, 2)
            },
            list);
        Assert.Equal(
            new List<SupportResponseView> { new SupportResponseView("Première", Now.AddHours(-1)), new SupportResponseView("Seconde", Now) },
            details!.Responses);
    }

    // Effacer une demande de plus d'un an emporte ses réponses et l'e-mail d'une réponse encore en attente. Une demande
    // envoyée il y a un an exactement est gardée, avec sa réponse.
    [Fact]
    public async Task OldRequestIsErasedWithItsResponsesAndTheirEmails()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountAsync(databaseContext, "test-support-effacement@exemple.test");
        var limit = SupportRules.FindOldestKeptSendingTime(Now);
        var tooOld = SupportRequest.FromUser(account.Id, "Trop ancienne", limit - TimeSpan.FromMicroseconds(1));
        var kept = SupportRequest.FromUser(account.Id, "Gardée", limit);
        var oldResponse = new SupportResponse { Content = "Réponse ancienne", SentAt = limit, SupportRequestId = tooOld.Id };
        var keptResponse = new SupportResponse { Content = "Réponse gardée", SentAt = Now, SupportRequestId = kept.Id };
        await SaveAsync(databaseContext, new[] { tooOld, kept }, oldResponse, keptResponse);
        await databaseContext.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO demande_email (type_demande_email, id_reponse) VALUES ('reponse_support', {oldResponse.Id}), ('reponse_support', {keptResponse.Id})");

        var deletedCount = await new SupportRequestRepository(databaseContext).DeleteSentBeforeAsync(limit);

        Assert.True(deletedCount >= 1);
        Assert.False(await databaseContext.SupportRequests.AnyAsync(row => row.Id == tooOld.Id));
        Assert.False(await databaseContext.SupportResponses.AnyAsync(row => row.Id == oldResponse.Id));
        Assert.False(await databaseContext.EmailRequests.AnyAsync(row => row.SupportResponseId == oldResponse.Id));
        Assert.True(await databaseContext.SupportRequests.AnyAsync(row => row.Id == kept.Id));
        Assert.True(await databaseContext.EmailRequests.AnyAsync(row => row.SupportResponseId == keptResponse.Id));
    }

    // L'e-mail d'une réponse part à l'adresse du compte de la demande telle qu'elle est maintenant, même si la demande avait aussi
    // une adresse de contact ; celle d'un visiteur part à son adresse de contact. Une réponse inconnue n'a pas d'e-mail.
    [Fact]
    public async Task ResponseEmailReadsTheCurrentAddressOfTheRequest()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveAccountAsync(databaseContext, "test-support-envoi@exemple.test");
        var fromAccount = new SupportRequest { Message = "Compte", ContactEmail = "test-support-contact@exemple.test", SentAt = Now.AddDays(-1), UserId = account.Id };
        var fromVisitor = SupportRequest.FromVisitor("test-support-visiteur@exemple.test", "Visiteur", Now.AddDays(-2));
        var accountResponse = SupportResponse.Create(fromAccount.Id, "Réponse au compte", Now);
        var visitorResponse = SupportResponse.Create(fromVisitor.Id, "Réponse au visiteur", Now);
        await SaveAsync(databaseContext, new[] { fromAccount, fromVisitor }, accountResponse, visitorResponse);
        await databaseContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE utilisateur SET email_utilisateur = 'test-support-nouvelle@exemple.test' WHERE id_utilisateur = {account.Id}");
        var repository = new SupportRequestRepository(databaseContext);

        Assert.Equal(
            new SupportResponseEmailContent("test-support-nouvelle@exemple.test", fromAccount.SentAt, "Réponse au compte", account.Id),
            await repository.ReadResponseEmailAsync(accountResponse.Id));
        Assert.Equal(
            new SupportResponseEmailContent("test-support-visiteur@exemple.test", fromVisitor.SentAt, "Réponse au visiteur", null),
            await repository.ReadResponseEmailAsync(visitorResponse.Id));
        Assert.Null(await repository.ReadResponseEmailAsync(Guid.NewGuid()));
    }
}
