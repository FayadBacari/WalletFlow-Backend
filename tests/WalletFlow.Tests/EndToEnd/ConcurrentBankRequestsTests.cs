using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Application.BankConnections;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Courses entre requêtes simultanées sur les connexions bancaires, avec la vraie API et la vraie base de test :
// chaque requête a son propre accès à la base et sa propre transaction, comme dans l'API. Seul le prestataire est faux.
[Collection(RealDatabaseCollection.Name)]
public class ConcurrentBankRequestsTests
{
    private const int SimultaneousRequestCount = 20;
    private static readonly UserPresence Presence = new UserPresence(IPAddress.Loopback, "Course");

    // Enregistre pour de vrai un compte confirmé.
    private static async Task<User> SaveAccountAsync(string email)
    {
        var account = new User { Email = email, PasswordHash = "empreinte-de-test", IsEmailConfirmed = true };

        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.Users.Add(account);
        await databaseContext.SaveChangesAsync();

        return account;
    }

    // Commence une autorisation avec le vrai cas d'utilisation, et renvoie le state que le faux prestataire a reçu.
    private static async Task<string> StartAsync(ApiFactoryWithRealDatabase api, Guid userId, Guid? renewedConnectionId = null)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var startService = scope.ServiceProvider.GetRequiredService<BankAuthorizationStartService>();
        var result = renewedConnectionId is Guid connectionId
            ? await startService.StartRenewalAsync(userId, connectionId)
            : await startService.StartNewAsync(userId, FakeBankDataProvider.BankName);
        Assert.IsType<BankAuthorizationStartResult.Started>(result);

        return api.BankProvider.AuthorizationRequests.Last().State;
    }

    // Termine une autorisation avec le vrai cas d'utilisation.
    private static async Task<BankConnectionFinishResult> FinishAsync(IServiceProvider services, Guid userId, string code, string state)
    {
        return await services.GetRequiredService<BankConnectionFinishService>().FinishAsync(userId, code, state, Presence);
    }

    // Connecte la banque des tests avec un compte, et renvoie l'identifiant de la connexion.
    private static async Task<Guid> ConnectAsync(ApiFactoryWithRealDatabase api, Guid userId, string code)
    {
        var state = await StartAsync(api, userId);
        api.BankProvider.AddSession(code, new TestBankAccount("stable-" + code, "Compte courant", "EUR", 10_000));

        await using var scope = api.Services.CreateAsyncScope();
        var result = await FinishAsync(scope.ServiceProvider, userId, code, state);

        return Assert.IsType<BankConnectionFinishResult.Connected>(result).ConnectionId;
    }

    // SS §7 : 20 retours de la banque en même temps avec le même state. Un seul crée la connexion.
    [Fact]
    public async Task SameStateFinishedSimultaneouslyCreatesOneConnection()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-retour-banque");

        try
        {
            var user = await SaveAccountAsync(email);
            var state = await StartAsync(api, user.Id);
            api.BankProvider.AddSession("code-unique", new TestBankAccount("stable-1", "Compte courant", "EUR", 10_000));

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, requestNumber) =>
                FinishAsync(services, user.Id, "code-unique", state));

            Assert.Equal(1, results.Count(result => result is BankConnectionFinishResult.Connected));
            Assert.Equal(SimultaneousRequestCount - 1, results.Count(result => result is BankConnectionFinishResult.LinkNoLongerValid));
            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            Assert.Equal(1, await databaseContext.BankConnections.CountAsync(row => row.UserId == user.Id));
            Assert.Equal(1, api.BankProvider.UsedCodeCount);
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // SS §6 : 20 mises à jour demandées en même temps par le même utilisateur. Une seule passe.
    [Fact]
    public async Task SimultaneousUpdatesOfTheSameUserAreLimitedToOne()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-mise-a-jour");

        try
        {
            var user = await SaveAccountAsync(email);
            await ConnectAsync(api, user.Id, "code-maj");

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, requestNumber) =>
                services.GetRequiredService<BankBalanceUpdateService>().UpdateForUserAsync(user.Id, Presence));

            Assert.Equal(1, results.Count(result => result is BankUpdateResult.Updated));
            Assert.Equal(SimultaneousRequestCount - 1, results.Count(result => result is BankUpdateResult.TooManyUpdates));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // GR Comptes : 20 mises à jour d'une connexion pendant sa déconnexion, trois manches. La déconnexion réussit, rien ne reste,
    // et aucune mise à jour n'échoue en panne : celles qui arrivent après ne trouvent plus rien.
    [Fact]
    public async Task UpdatesDuringADisconnectionLeaveNothingBehind()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-deconnexion");

        try
        {
            var user = await SaveAccountAsync(email);

            for (var round = 1; round <= 3; round++)
            {
                var connectionId = await ConnectAsync(api, user.Id, "code-manche-" + round);

                var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount + 1, async (services, requestNumber) =>
                {
                    if (requestNumber == 0)
                    {
                        var result = await services.GetRequiredService<BankDisconnectionService>().DisconnectAsync(user.Id, connectionId, IPAddress.Loopback);
                        return result is BankDisconnectionResult.Disconnected;
                    }

                    await services.GetRequiredService<BankBalanceUpdateService>().UpdateConnectionAsync(connectionId, null);
                    return true;
                });

                Assert.All(results, Assert.True);
                await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
                Assert.False(await databaseContext.BankConnections.AnyAsync(row => row.Id == connectionId));
                Assert.False(await databaseContext.BankAccounts.AnyAsync(row => row.BankConnectionId == connectionId));
            }
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // SS §7 : un renouvellement et une déconnexion en même temps, cinq manches. À la fin, la connexion a disparu,
    // et chaque session ouverte chez le prestataire, l'ancienne comme la nouvelle, est révoquée.
    [Fact]
    public async Task RenewalDuringADisconnectionLeavesNoSessionOpen()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-renouvellement");

        try
        {
            var user = await SaveAccountAsync(email);

            for (var round = 1; round <= 5; round++)
            {
                var connectionId = await ConnectAsync(api, user.Id, "code-depart-" + round);
                await using var readContext = LocalTestDatabase.CreateDatabaseContext();
                var oldSessionId = (await readContext.BankConnections.AsNoTracking().SingleAsync(row => row.Id == connectionId)).ExternalConnectionId;
                var state = await StartAsync(api, user.Id, connectionId);
                var newSession = api.BankProvider.AddSession("code-renouvellement-" + round, new TestBankAccount("stable-code-depart-" + round, "Compte courant", "EUR", 20_000));

                await SimultaneousRequests.RunAsync(api, 2, async (services, requestNumber) =>
                {
                    if (requestNumber == 0)
                    {
                        await FinishAsync(services, user.Id, "code-renouvellement-" + round, state);
                    }
                    else
                    {
                        await services.GetRequiredService<BankDisconnectionService>().DisconnectAsync(user.Id, connectionId, IPAddress.Loopback);
                    }

                    return true;
                });

                await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
                Assert.False(await databaseContext.BankConnections.AnyAsync(row => row.Id == connectionId));
                Assert.Contains(oldSessionId, api.BankProvider.RevokedSessions);
                Assert.Contains(newSession.SessionId, api.BankProvider.RevokedSessions);
            }
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }
}
