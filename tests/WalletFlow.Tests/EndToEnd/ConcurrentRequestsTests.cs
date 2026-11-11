using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Envoie de vraies requêtes simultanées à la vraie API, sur la vraie base de test : chacune a son propre accès à la base
// et sa propre transaction, comme dans l'API. Ces courses ont trouvé des failles pendant les étapes 4.3 à 4.5 :
// elles restent ici pour qu'une régression du contrôle de concurrence se voie aussitôt.
[Collection(RealDatabaseCollection.Name)]
public class ConcurrentRequestsTests
{
    private const int SimultaneousRequestCount = 20;

    // 20 mauvais mots de passe en même temps : exactement 5 essais sont comptés, le compte se bloque une seule fois,
    // et un seul e-mail de déblocage part (GR Auth, SS §2). Sans contrôle de concurrence, des essais se perdent.
    [Fact]
    public async Task SimultaneousWrongPasswordsCountExactlyFiveAttemptsAndLockTheAccountOnce()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-essais");

        try
        {
            var account = await SaveConfirmedAccountAsync(api, email);

            var results = await RunSimultaneouslyAsync(api, (services, requestNumber) =>
                services.GetRequiredService<AccountLoginService>().LogInAsync(new LoginAttempt
                {
                    EmailAddress = email,
                    Password = "Mauvais-Mot-De-Passe-" + requestNumber
                }));

            Assert.All(results, result => Assert.IsType<AccountLoginResult.WrongCredentials>(result));
            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            var savedAccount = await databaseContext.Users.SingleAsync(row => row.Id == account.Id);
            Assert.Equal(5, savedAccount.FailedLoginAttemptCount);
            Assert.Equal(1, savedAccount.LockoutCount);
            Assert.NotNull(savedAccount.LockoutEndsAt);
            Assert.Equal(1, await CountEventsAsync(databaseContext, account.Id, SecurityEventType.Lockout, SecurityEventResult.Success));
            Assert.Equal(SimultaneousRequestCount, await CountEventsAsync(databaseContext, account.Id, SecurityEventType.Login, SecurityEventResult.Failure));
            await WaitForEmailsAsync(api, email);
            Assert.Equal(1, api.EmailSender.CountSentEmailsTo(email));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // Le même lien de récupération envoyé 20 fois en même temps : un seul changement réussit, car le lien ne sert qu'une fois (SS §2),
    // et le compte garde le mot de passe de la seule requête qui a réussi.
    [Fact]
    public async Task SameRecoveryLinkUsedSimultaneouslyChangesThePasswordOnlyOnce()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-lien");

        try
        {
            var account = await SaveConfirmedAccountAsync(api, email);
            var linkToken = await SaveRecoveryLinkAsync(api, account);

            var results = await RunSimultaneouslyAsync(api, (services, requestNumber) =>
                services.GetRequiredService<PasswordResetService>().ResetAsync(linkToken, "Nouveau-Mot-De-Passe-" + requestNumber, null));

            Assert.Equal(1, results.Count(result => result == PasswordResetResult.PasswordChanged));
            Assert.Equal(SimultaneousRequestCount - 1, results.Count(result => result == PasswordResetResult.LinkNoLongerValid));
            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            Assert.Equal(1, await CountEventsAsync(databaseContext, account.Id, SecurityEventType.PasswordChange, SecurityEventResult.Success));
            var savedAccount = await databaseContext.Users.SingleAsync(row => row.Id == account.Id);
            var winningRequestNumber = results.IndexOf(PasswordResetResult.PasswordChanged);
            var passwordHasher = api.Services.GetRequiredService<IPasswordHasher>();
            Assert.True(await passwordHasher.VerifyAsync("Nouveau-Mot-De-Passe-" + winningRequestNumber, savedAccount.PasswordHash));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // Lance toutes les requêtes au même signal, chacune dans sa propre portée : son propre accès à la base, comme une requête de l'API.
    // Les résultats sont rendus dans l'ordre des numéros de requête.
    private static async Task<List<TResult>> RunSimultaneouslyAsync<TResult>(
        ApiFactoryWithRealDatabase api,
        Func<IServiceProvider, int, Task<TResult>> sendRequest)
    {
        var startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<Task<TResult>>();

        for (var requestNumber = 0; requestNumber < SimultaneousRequestCount; requestNumber++)
        {
            var currentRequestNumber = requestNumber;
            requests.Add(Task.Run(async () =>
            {
                await startSignal.Task;
                await using var scope = api.Services.CreateAsyncScope();
                return await sendRequest(scope.ServiceProvider, currentRequestNumber);
            }));
        }

        startSignal.SetResult();
        var results = await Task.WhenAll(requests);

        return new List<TResult>(results);
    }

    // Enregistre pour de vrai un compte confirmé avec le mot de passe des tests, haché avec le vrai Argon2.
    private static async Task<User> SaveConfirmedAccountAsync(ApiFactoryWithRealDatabase api, string email)
    {
        var passwordHasher = api.Services.GetRequiredService<IPasswordHasher>();
        var account = new User
        {
            Email = email,
            PasswordHash = await passwordHasher.HashAsync(PasswordRules.Normalize(ValidPassword)),
            IsEmailConfirmed = true
        };

        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.Users.Add(account);
        await databaseContext.SaveChangesAsync();

        return account;
    }

    // Enregistre pour de vrai un lien de récupération valable pour ce compte, et renvoie le jeton écrit dans ce lien.
    private static async Task<string> SaveRecoveryLinkAsync(ApiFactoryWithRealDatabase api, User account)
    {
        var linkToken = api.Services.GetRequiredService<ISecureTokenGenerator>().GenerateToken();

        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.EmailTokens.Add(new EmailToken
        {
            Type = EmailTokenType.PasswordRecovery,
            TokenHash = linkToken.Hash,
            ExpiresAt = DateTime.UtcNow + EmailTokenValidityDurations.PasswordRecoveryLink,
            UserId = account.Id
        });
        await databaseContext.SaveChangesAsync();

        return linkToken.Token;
    }

    // Compte les événements de ce type et de ce résultat dans le vrai journal, pour ce compte.
    private static async Task<int> CountEventsAsync(
        WalletFlowDatabaseContext databaseContext,
        Guid accountId,
        SecurityEventType type,
        SecurityEventResult result)
    {
        return await databaseContext.SecurityEvents.CountAsync(row =>
            row.UserId == accountId
            && row.Type == type
            && row.Result == result);
    }

    // Attend que la tâche de fond ait envoyé un e-mail à cette adresse, 10 secondes au plus.
    private static async Task WaitForEmailsAsync(ApiFactoryWithRealDatabase api, string email)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (api.EmailSender.CountSentEmailsTo(email) == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }
}
