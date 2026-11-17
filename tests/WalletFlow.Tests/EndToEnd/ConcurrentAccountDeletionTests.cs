using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Application.AccountDeletion;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Courses entre la connexion et l'effacement d'un compte, avec la vraie API et la vraie base de test (GR Suppression, SS §7) :
// chaque requête a son propre accès à la base et sa propre transaction, comme dans l'API.
[Collection(RealDatabaseCollection.Name)]
public class ConcurrentAccountDeletionTests
{
    private const int AccountCount = 10;
    private const string CancelledSubject = "La suppression de ton compte WalletFlow est annulée";
    private const string ErasedSubject = "Ton compte WalletFlow est effacé";

    // Sur 10 comptes dont l'effacement est dû, une connexion avec le bon mot de passe, une avec un mauvais et l'effacement partent
    // au même instant. Pour chaque compte, un seul des deux gagne : ou la connexion annule la demande et le compte reste, ou il
    // est effacé et la connexion est refusée comme pour un compte inconnu. Aucune requête ne tombe en erreur, et seul l'e-mail du
    // gagnant part. L'effacement part 50 ms plus tard d'un compte à l'autre, et la vérification d'Argon2 retarde la connexion :
    // l'effacement gagne sur les premiers comptes, la connexion sur les derniers, et la course se joue entre les deux.
    [Fact]
    public async Task LoginAndErasureAtTheSameInstantHaveOneWinner()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var emails = Enumerable.Range(0, AccountCount).Select(_ => CreateUniqueEmail("course-effacement")).ToList();

        try
        {
            var accounts = new List<User>();
            foreach (var email in emails)
            {
                accounts.Add(await SaveAccountToEraseAsync(api, email, 0));
            }

            var results = await SimultaneousRequests.RunAsync<object>(api, AccountCount * 3, async (services, requestNumber) =>
            {
                var accountNumber = requestNumber / 3;

                if (requestNumber % 3 == 0)
                {
                    return await LogInAsync(services, emails[accountNumber], ValidPassword);
                }

                if (requestNumber % 3 == 1)
                {
                    return await LogInAsync(services, emails[accountNumber], "Mauvais-Mot-De-Passe-1");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50 * accountNumber));
                return await services.GetRequiredService<AccountErasureService>().EraseAsync(accounts[accountNumber].Id);
            });

            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            for (var accountNumber = 0; accountNumber < AccountCount; accountNumber++)
            {
                var email = emails[accountNumber];
                var isErased = (bool)results[accountNumber * 3 + 2];
                var savedAccount = await databaseContext.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == accounts[accountNumber].Id);
                Assert.IsType<AccountLoginResult.WrongCredentials>(results[accountNumber * 3 + 1]);

                if (isErased)
                {
                    Assert.IsType<AccountLoginResult.WrongCredentials>(results[accountNumber * 3]);
                    Assert.Null(savedAccount);
                }
                else
                {
                    Assert.True(Assert.IsType<AccountLoginResult.LoggedIn>(results[accountNumber * 3]).HasCancelledDeletion);
                    Assert.NotNull(savedAccount);
                    Assert.Null(savedAccount.DeletionRequestedAt);
                }

                await WaitUntilQueueIsEmptyAsync(databaseContext, accounts[accountNumber].Id, email);
                Assert.Equal(
                    (isErased ? 0 : 1, isErased ? 1 : 0),
                    (api.EmailSender.FindSentEmailsTo(email, CancelledSubject).Count, api.EmailSender.FindSentEmailsTo(email, ErasedSubject).Count));
            }
        }
        finally
        {
            foreach (var email in emails)
            {
                await DeleteAccountAsync(email);
                await DeleteQueuedEmailsToAsync(email);
            }
        }
    }

    // Sur 10 comptes dont l'effacement est dû, et à qui il ne reste qu'un essai avant le blocage, le mauvais mot de passe qui
    // les bloque et l'effacement partent au même instant. L'effacement gagne toujours, et la connexion est refusée sans erreur.
    // L'effacement part un peu plus tard d'un compte à l'autre : il tombe parfois entre l'essai compté et la trace du blocage.
    [Fact]
    public async Task LockoutAtTheInstantOfTheErasureDoesNotStopIt()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var emails = Enumerable.Range(0, AccountCount).Select(_ => CreateUniqueEmail("course-blocage-effacement")).ToList();

        try
        {
            var accounts = new List<User>();
            foreach (var email in emails)
            {
                accounts.Add(await SaveAccountToEraseAsync(api, email, AccountLockoutRules.MaximumFailedAttempts - 1));
            }

            var results = await SimultaneousRequests.RunAsync<object>(api, AccountCount * 2, async (services, requestNumber) =>
            {
                var accountNumber = requestNumber / 2;

                if (requestNumber % 2 == 0)
                {
                    return await LogInAsync(services, emails[accountNumber], "Mauvais-Mot-De-Passe-1");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(15 * accountNumber));
                return await services.GetRequiredService<AccountErasureService>().EraseAsync(accounts[accountNumber].Id);
            });

            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            for (var accountNumber = 0; accountNumber < AccountCount; accountNumber++)
            {
                Assert.IsType<AccountLoginResult.WrongCredentials>(results[accountNumber * 2]);
                Assert.True((bool)results[accountNumber * 2 + 1]);
                Assert.False(await databaseContext.Users.AnyAsync(row => row.Id == accounts[accountNumber].Id));
                await WaitUntilQueueIsEmptyAsync(databaseContext, accounts[accountNumber].Id, emails[accountNumber]);
                Assert.Single(api.EmailSender.FindSentEmailsTo(emails[accountNumber], ErasedSubject));
            }
        }
        finally
        {
            foreach (var email in emails)
            {
                await DeleteAccountAsync(email);
                await DeleteQueuedEmailsToAsync(email);
            }
        }
    }

    // Se connecte avec le vrai cas d'utilisation, depuis un appareil inconnu.
    private static async Task<AccountLoginResult> LogInAsync(IServiceProvider services, string email, string password)
    {
        return await services.GetRequiredService<AccountLoginService>().LogInAsync(new LoginAttempt
        {
            EmailAddress = email,
            Password = password
        });
    }

    // Enregistre pour de vrai un compte confirmé, avec le mot de passe des tests haché avec le vrai Argon2, ses mauvais essais
    // déjà comptés, et sa suppression demandée depuis un peu plus de 30 jours : son effacement est dû.
    private static async Task<User> SaveAccountToEraseAsync(ApiFactoryWithRealDatabase api, string email, int failedAttemptCount)
    {
        var passwordHasher = api.Services.GetRequiredService<IPasswordHasher>();
        var account = new User
        {
            Email = email,
            PasswordHash = await passwordHasher.HashAsync(PasswordRules.Normalize(ValidPassword)),
            IsEmailConfirmed = true,
            FailedLoginAttemptCount = (short)failedAttemptCount,
            DeletionRequestedAt = DateTime.UtcNow - AccountDeletionRules.ErasureDelay - TimeSpan.FromHours(1)
        };

        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.Users.Add(account);
        await databaseContext.SaveChangesAsync();

        return account;
    }

    // Attend que la tâche de fond ait vidé la file de ce compte et de son adresse, 10 secondes au plus.
    private static async Task WaitUntilQueueIsEmptyAsync(WalletFlowDatabaseContext databaseContext, Guid accountId, string email)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (await databaseContext.EmailRequests.AnyAsync(row => row.UserId == accountId || row.EmailAddress == email) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.False(await databaseContext.EmailRequests.AnyAsync(row => row.UserId == accountId || row.EmailAddress == email));
    }
}
