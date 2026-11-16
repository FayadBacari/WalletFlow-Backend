using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Application.Administration;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Courses entre actions simultanées de l'administrateur sur la même demande de support (GR Admin, SS §6 et §11), avec le vrai
// cas d'utilisation et la vraie base de test. Le rôle d'administrateur ne s'attribue que par fn_changer_role, que la base de test
// refuse à l'API : le compte qui agit ici est un compte ordinaire. Le rôle est vérifié par le filtre des routes, testé à part.
[Collection(RealDatabaseCollection.Name)]
public class ConcurrentAdministrationRequestsTests
{
    // 20 résolutions en même temps de la même demande : une seule la résout et est notée au journal, les 19 autres trouvent
    // la demande déjà résolue.
    [Fact]
    public async Task SimultaneousResolutionsResolveTheRequestOnce()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var administratorEmail = CreateUniqueEmail("course-admin-resolution");
        var request = SupportRequest.FromVisitor(CreateUniqueEmail("course-visiteur"), "Bonjour", DateTime.UtcNow.AddHours(-1));

        try
        {
            var administratorId = await SaveRowsAsync(administratorEmail, request);

            var results = await SimultaneousRequests.RunAsync(api, 20, (services, requestNumber) =>
                services.GetRequiredService<AdminSupportService>().ResolveAsync(administratorId, request.Id, null));

            Assert.Equal(1, results.Count(result => result is AdminSupportResolutionResult.Resolved));
            Assert.Equal(19, results.Count(result => result is AdminSupportResolutionResult.AlreadyResolved));
            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            Assert.Equal(SupportRequestStatus.Resolved, (await databaseContext.SupportRequests.SingleAsync(row => row.Id == request.Id)).Status);
            Assert.Equal(1, await CountAdminActionsAsync(databaseContext, administratorId));
        }
        finally
        {
            await DeleteRowsAsync(administratorEmail, request.Id);
        }
    }

    // 5 réponses en même temps à la même demande : la ligne de la demande, réécrite à chaque réponse, les fait passer l'une après
    // l'autre. Toutes sont enregistrées, notées au journal, et partent chacune dans un seul e-mail à l'adresse du visiteur.
    [Fact]
    public async Task SimultaneousAnswersAreAllSavedAndSentOnce()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var administratorEmail = CreateUniqueEmail("course-admin-reponses");
        var visitorEmail = CreateUniqueEmail("course-visiteur");
        var request = SupportRequest.FromVisitor(visitorEmail, "Bonjour", DateTime.UtcNow.AddHours(-1));
        const int answerCount = 5;

        try
        {
            var administratorId = await SaveRowsAsync(administratorEmail, request);

            var results = await SimultaneousRequests.RunAsync(api, answerCount, (services, requestNumber) =>
                services.GetRequiredService<AdminSupportService>().AnswerAsync(administratorId, request.Id, "Réponse numéro " + requestNumber + ".", null));

            Assert.All(results, result => Assert.IsType<AdminSupportAnswerResult.Answered>(result));
            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            Assert.Equal(SupportRequestStatus.InProgress, (await databaseContext.SupportRequests.SingleAsync(row => row.Id == request.Id)).Status);
            Assert.Equal(answerCount, await databaseContext.SupportResponses.CountAsync(row => row.SupportRequestId == request.Id));
            Assert.Equal(answerCount, await CountAdminActionsAsync(databaseContext, administratorId));
            await WaitUntilResponsesAreSentAsync(databaseContext, request.Id);
            var sentBodies = api.EmailSender.SentEmails.Where(email => email.RecipientAddress == visitorEmail).Select(email => email.Body).ToList();
            Assert.Equal(answerCount, sentBodies.Count);
            Assert.All(Enumerable.Range(0, answerCount), number =>
                Assert.Single(sentBodies, body => body.Contains("Réponse numéro " + number + ".")));
        }
        finally
        {
            await DeleteRowsAsync(administratorEmail, request.Id);
        }
    }

    // Enregistre pour de vrai le compte qui agit et la demande du visiteur. Renvoie l'identifiant du compte.
    private static async Task<Guid> SaveRowsAsync(string administratorEmail, SupportRequest request)
    {
        var administrator = new User { Email = administratorEmail, PasswordHash = "empreinte-de-test", IsEmailConfirmed = true };

        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.Users.Add(administrator);
        databaseContext.SupportRequests.Add(request);
        await databaseContext.SaveChangesAsync();

        return administrator.Id;
    }

    // Efface la demande du visiteur, avec ses réponses et leurs e-mails (ON DELETE CASCADE), puis le compte qui a agi.
    private static async Task DeleteRowsAsync(string administratorEmail, Guid requestId)
    {
        await using (var databaseContext = LocalTestDatabase.CreateDatabaseContext())
        {
            await databaseContext.SupportRequests.Where(row => row.Id == requestId).ExecuteDeleteAsync();
        }

        await DeleteAccountAsync(administratorEmail);
    }

    // Compte les actions d'administrateur de ce compte dans le vrai journal.
    private static async Task<int> CountAdminActionsAsync(WalletFlowDatabaseContext databaseContext, Guid administratorId)
    {
        return await databaseContext.SecurityEvents.CountAsync(row =>
            row.UserId == administratorId
            && row.Type == SecurityEventType.AdminAction
            && row.Result == SecurityEventResult.Success);
    }

    // Attend que la tâche de fond ait envoyé les e-mails des réponses de cette demande, 10 secondes au plus.
    private static async Task WaitUntilResponsesAreSentAsync(WalletFlowDatabaseContext databaseContext, Guid requestId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var waitingEmails = databaseContext.EmailRequests.Where(row =>
            databaseContext.SupportResponses.Any(response => response.Id == row.SupportResponseId && response.SupportRequestId == requestId));

        while (await waitingEmails.AnyAsync() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.False(await waitingEmails.AnyAsync());
    }
}
