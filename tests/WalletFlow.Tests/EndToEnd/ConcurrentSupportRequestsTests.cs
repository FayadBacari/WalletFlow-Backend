using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Application.Support;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Course entre demandes de support simultanées, avec le vrai cas d'utilisation et la vraie base de test : chaque requête a son
// propre accès à la base et sa propre transaction, comme dans l'API (SS §6).
[Collection(RealDatabaseCollection.Name)]
public class ConcurrentSupportRequestsTests
{
    private const int SimultaneousRequestCount = 20;

    // SS §6 : 20 demandes en même temps, à 3 demandes déjà envoyées. Exactement 7 passent, et le compte en garde 10 :
    // la ligne du compte, réécrite avec sa version, fait passer les demandes une à une.
    [Fact]
    public async Task SimultaneousRequestsNeverGoBeyondTheLimit()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-support-limite");

        try
        {
            var account = new User { Email = email, PasswordHash = "empreinte-de-test", IsEmailConfirmed = true };
            await using (var setupContext = LocalTestDatabase.CreateDatabaseContext())
            {
                setupContext.Users.Add(account);
                setupContext.SupportRequests.AddRange(Enumerable.Range(1, 3)
                    .Select(number => SupportRequest.FromUser(account.Id, "Déjà envoyée " + number, DateTime.UtcNow.AddHours(-1))));
                await setupContext.SaveChangesAsync();
            }

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, requestNumber) =>
                services.GetRequiredService<SupportRequestSendingService>().SendAsync(account.Id, "Demande " + requestNumber));

            await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
            Assert.Equal(7, results.Count(result => result is SupportRequestSendingResult.Sent));
            Assert.Equal(13, results.Count(result => result is SupportRequestSendingResult.TooManyRequests));
            Assert.Equal(SupportRules.MaximumRequestsPerDay, await databaseContext.SupportRequests.CountAsync(row => row.UserId == account.Id));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }
}
