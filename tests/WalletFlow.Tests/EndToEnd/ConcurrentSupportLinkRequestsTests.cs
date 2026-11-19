using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Application.Security;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Course sur le lien du support qui désactive la double authentification (GR Auth, SS §2), avec le vrai cas d'utilisation,
// le vrai Argon2 et la vraie base de test : chaque requête a son propre accès à la base et sa propre transaction, comme dans l'API.
[Collection(RealDatabaseCollection.Name)]
public class ConcurrentSupportLinkRequestsTests
{
    private const int SimultaneousRequestCount = 20;

    // 20 mauvais mots de passe en même temps sur le même lien : exactement 5 essais sont comptés et vérifiés, le 5e annule le lien,
    // et les 15 autres sont refusés sans vérification. Sans le lien réécrit avec sa version, des essais se perdraient.
    [Fact]
    public async Task SimultaneousWrongPasswordsAreCountedFiveTimesAtMost()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-lien-double-auth");

        try
        {
            var account = await SaveAccountWithTwoFactorAsync(api, email);
            var (linkId, linkToken) = await SaveSupportLinkAsync(api, account);

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, requestNumber) =>
                services.GetRequiredService<TwoFactorSupportLinkService>().DeactivateAsync(linkToken, "Mauvais-Mot-De-Passe-" + requestNumber, null));

            Assert.Equal(4, results.Count(result => result is TwoFactorSupportLinkResult.WrongPassword));
            Assert.Equal(1, results.Count(result => result is TwoFactorSupportLinkResult.LinkCancelled));
            Assert.Equal(15, results.Count(result => result is TwoFactorSupportLinkResult.LinkNoLongerValid));
            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            var savedLink = await databaseContext.EmailTokens.SingleAsync(row => row.Id == linkId);
            Assert.Equal((5, (DateTime?)null), (savedLink.PasswordTryCount, savedLink.UsedAt));
            Assert.True((await databaseContext.Users.SingleAsync(row => row.Id == account.Id)).IsTwoFactorEnabled);
            Assert.Equal(5, await CountEventsAsync(databaseContext, account.Id, SecurityEventResult.Failure));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // Le même lien envoyé 20 fois en même temps avec le bon mot de passe : la double authentification est désactivée une seule
    // fois, notée une seule fois au journal, avec une seule alerte, et toutes les sessions du compte sont fermées.
    [Fact]
    public async Task SameLinkWithTheRightPasswordDeactivatesOnlyOnce()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-lien-double-auth-ok");

        try
        {
            var account = await SaveAccountWithTwoFactorAsync(api, email);
            var (linkId, linkToken) = await SaveSupportLinkAsync(api, account);
            await using (var setupContext = LocalTestDatabase.CreateDatabaseContext())
            {
                setupContext.UserSessions.AddRange(
                    new UserSession { CreatedAt = DateTime.UtcNow, Id = "course-" + Guid.NewGuid().ToString("N"), LastActivityAt = DateTime.UtcNow, UserId = account.Id },
                    new UserSession { CreatedAt = DateTime.UtcNow, Id = "course-" + Guid.NewGuid().ToString("N"), LastActivityAt = DateTime.UtcNow, UserId = account.Id });
                await setupContext.SaveChangesAsync();
            }

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, requestNumber) =>
                services.GetRequiredService<TwoFactorSupportLinkService>().DeactivateAsync(linkToken, ValidPassword, null));

            Assert.Equal(1, results.Count(result => result is TwoFactorSupportLinkResult.Deactivated));
            Assert.Equal(SimultaneousRequestCount - 1, results.Count(result => result is TwoFactorSupportLinkResult.LinkNoLongerValid));
            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            var savedAccount = await databaseContext.Users.SingleAsync(row => row.Id == account.Id);
            Assert.Equal((false, (string?)null), (savedAccount.IsTwoFactorEnabled, savedAccount.EncryptedTotpSecret));
            Assert.NotNull((await databaseContext.EmailTokens.SingleAsync(row => row.Id == linkId)).UsedAt);
            Assert.False(await databaseContext.UserSessions.AnyAsync(row => row.UserId == account.Id));
            Assert.Equal(1, await CountEventsAsync(databaseContext, account.Id, SecurityEventResult.Success));
            await WaitUntilQueueIsEmptyAsync(databaseContext, account.Id);
            Assert.Equal(1, api.EmailSender.CountSentEmailsTo(email));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // Enregistre pour de vrai un compte confirmé avec la double authentification, et le mot de passe des tests haché avec le vrai
    // Argon2. Le compte n'a aucun changement de mot de passe au journal : la règle des 7 jours le laisse passer.
    private static async Task<User> SaveAccountWithTwoFactorAsync(ApiFactoryWithRealDatabase api, string email)
    {
        var account = new User
        {
            Email = email,
            PasswordHash = await api.Services.GetRequiredService<IPasswordHasher>().HashAsync(PasswordRules.Normalize(ValidPassword)),
            IsEmailConfirmed = true,
            IsTwoFactorEnabled = true,
            EncryptedTotpSecret = "secret-chiffre-de-test"
        };

        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.Users.Add(account);
        await databaseContext.SaveChangesAsync();

        return account;
    }

    // Enregistre pour de vrai le lien que le support a fait envoyer à ce compte. Renvoie son identifiant et le jeton écrit dans le lien.
    private static async Task<(Guid LinkId, string Token)> SaveSupportLinkAsync(ApiFactoryWithRealDatabase api, User account)
    {
        var generated = api.Services.GetRequiredService<ISecureTokenGenerator>().GenerateToken();
        var link = new EmailToken
        {
            Type = EmailTokenType.TwoFactorDeactivation,
            TokenHash = generated.Hash,
            ExpiresAt = DateTime.UtcNow + EmailTokenValidityDurations.TwoFactorDeactivationLink,
            UserId = account.Id
        };

        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.EmailTokens.Add(link);
        await databaseContext.SaveChangesAsync();

        return (link.Id, generated.Token);
    }

    // Compte les désactivations de la double authentification de ce résultat dans le vrai journal, pour ce compte.
    private static async Task<int> CountEventsAsync(WalletFlowDatabaseContext databaseContext, Guid accountId, SecurityEventResult result)
    {
        return await databaseContext.SecurityEvents.CountAsync(row =>
            row.UserId == accountId
            && row.Type == SecurityEventType.TwoFactorDeactivation
            && row.Result == result);
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
}
