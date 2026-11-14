using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Application.Suggestions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Infrastructure;
using static WalletFlow.Tests.EndToEnd.TestAccountCleanup;

namespace WalletFlow.Tests.EndToEnd;

// Courses entre requêtes simultanées sur les suggestions, avec les vrais cas d'utilisation et la vraie base de test :
// chaque requête a son propre accès à la base et sa propre transaction, comme dans l'API (SS §5).
[Collection(RealDatabaseCollection.Name)]
public class ConcurrentSuggestionRequestsTests
{
    private const int SimultaneousRequestCount = 20;

    // Enregistre pour de vrai un compte confirmé avec ses catégories, un compte bancaire de 1 000 €, un sac « Épargne »,
    // et dans le mois évalué 2 000 € de revenus dès son premier jour et 1 700 € de loyer. Renvoie le compte et le sac.
    private static async Task<(User Account, Bag Bag)> SaveAccountWithDataAsync(string email, DateOnly month)
    {
        var account = new User { Email = email, PasswordHash = "empreinte-de-test", IsEmailConfirmed = true };
        var categories = DefaultCategories.CreateForUser(account.Id);
        var connection = new BankConnection
        {
            ExternalConnectionId = "connexion-" + Guid.NewGuid(),
            BankName = "Banque de test",
            AuthorizationExpiresAt = DateTime.UtcNow.AddDays(180),
            UserId = account.Id
        };
        var bankAccount = new BankAccount
        {
            ExternalAccountId = "compte-" + Guid.NewGuid(),
            Name = "Compte courant",
            BalanceInCents = 100_000,
            BalanceUpdatedAt = DateTime.UtcNow,
            BankConnectionId = connection.Id
        };
        var bag = new Bag { Name = "Épargne", UserId = account.Id };
        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        databaseContext.Add(account);
        databaseContext.Categories.AddRange(categories);
        databaseContext.AddRange(connection, bankAccount, bag);
        databaseContext.BankTransactions.AddRange(
            BankTestRows.CreateTransaction(bankAccount.Id, categories.Single(row => row.Name == "Revenus").Id, "salaire-" + Guid.NewGuid(), "SALAIRE", 200_000, month),
            BankTestRows.CreateTransaction(bankAccount.Id, categories.Single(row => row.Name == "Logement").Id, "loyer-" + Guid.NewGuid(), "LOYER", -170_000, month.AddDays(4)));
        await databaseContext.SaveChangesAsync();

        return (account, bag);
    }

    // Le mois évalué le jour où le test tourne.
    private static DateOnly EvaluatedMonth()
    {
        return SuggestionRules.FindEvaluatedMonth(UsersCalendar.FindToday(DateTimeOffset.UtcNow));
    }

    // Lit en base les suggestions et les mouvements du compte.
    private static async Task<(List<SavingsSuggestion> Suggestions, List<BagMovement> Movements)> ReadSavedAsync(Guid userId, Guid bagId)
    {
        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();
        var suggestions = await databaseContext.SavingsSuggestions.AsNoTracking().Where(row => row.UserId == userId).ToListAsync();
        var movements = await databaseContext.BagMovements.AsNoTracking().Where(row => row.DestinationBagId == bagId).ToListAsync();

        return (suggestions, movements);
    }

    // Calcule puis lit les suggestions avec le vrai cas d'utilisation, dans la portée de la requête.
    private static async Task<SuggestionList> ListAsync(IServiceProvider services, Guid userId)
    {
        return await services.GetRequiredService<SuggestionListService>().ListAsync(userId);
    }

    // GR Sacs : 20 premières lectures en même temps ne calculent le mois qu'une fois. Toutes voient les deux mêmes suggestions,
    // et la base n'en garde que deux. Sans le contrôle de la ligne de l'utilisateur, chaque lecture calculerait le mois,
    // et les index uniques refuseraient les doublons.
    [Fact]
    public async Task SimultaneousFirstReadsCalculateTheMonthOnce()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-suggestions-calcul");

        try
        {
            var (account, bag) = await SaveAccountWithDataAsync(email, EvaluatedMonth());

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, _) => ListAsync(services, account.Id));

            var (saved, _) = await ReadSavedAsync(account.Id, bag.Id);
            Assert.Equal(2, saved.Count);
            var savedIds = saved.Select(row => row.Id).OrderBy(id => id).ToList();
            Assert.All(results, result => Assert.Equal(savedIds, result.Suggestions.Select(view => view.Id).OrderBy(id => id).ToList()));
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // SS §5 : 20 clics simultanés sur « accepter » la même suggestion ne créent qu'un seul mouvement. Les autres trouvent
    // la suggestion déjà acceptée.
    [Fact]
    public async Task SimultaneousAcceptancesCreateOneMovement()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-suggestions-accepter");

        try
        {
            var (account, bag) = await SaveAccountWithDataAsync(email, EvaluatedMonth());
            await using (var scope = api.Services.CreateAsyncScope())
            {
                await ListAsync(scope.ServiceProvider, account.Id);
            }
            var suggestion = (await ReadSavedAsync(account.Id, bag.Id)).Suggestions.Single(row => row.Rule == SuggestionRule.UnallocatedMoney);

            var results = await SimultaneousRequests.RunAsync(api, SimultaneousRequestCount, (services, _) =>
                services.GetRequiredService<SuggestionAcceptanceService>().AcceptAsync(account.Id, suggestion.Id, bag.Id, 10_000));

            Assert.Equal(1, results.Count(result => result is SuggestionAcceptanceResult.Accepted));
            Assert.Equal(SimultaneousRequestCount - 1, results.Count(result => result is SuggestionAcceptanceResult.AlreadyAnswered));
            var (_, movements) = await ReadSavedAsync(account.Id, bag.Id);
            Assert.Equal(suggestion.Id, Assert.Single(movements).SavingsSuggestionId);
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }

    // « Accepter » et « ignorer » au même instant, 10 fois chacun : une seule réponse gagne. Si c'est « accepter », un seul
    // mouvement existe ; si c'est « ignorer », aucun.
    [Fact]
    public async Task AcceptAndIgnoreAtTheSameMomentGiveOneAnswer()
    {
        using var api = new ApiFactoryWithRealDatabase();
        var email = CreateUniqueEmail("course-suggestions-reponses");

        try
        {
            var (account, bag) = await SaveAccountWithDataAsync(email, EvaluatedMonth());
            await using (var scope = api.Services.CreateAsyncScope())
            {
                await ListAsync(scope.ServiceProvider, account.Id);
            }
            var suggestion = (await ReadSavedAsync(account.Id, bag.Id)).Suggestions.Single(row => row.Rule == SuggestionRule.UnallocatedMoney);

            var results = await SimultaneousRequests.RunAsync<object>(api, SimultaneousRequestCount, async (services, requestNumber) =>
                requestNumber % 2 == 0
                    ? await services.GetRequiredService<SuggestionAcceptanceService>().AcceptAsync(account.Id, suggestion.Id, bag.Id, 10_000)
                    : await services.GetRequiredService<SuggestionDismissalService>().DismissAsync(account.Id, suggestion.Id));

            var winners = results.Count(result => result is SuggestionAcceptanceResult.Accepted or SuggestionDismissalResult.Dismissed);
            Assert.Equal(1, winners);
            Assert.Equal(SimultaneousRequestCount - 1, results.Count(result =>
                result is SuggestionAcceptanceResult.AlreadyAnswered or SuggestionDismissalResult.AlreadyAnswered));
            var (saved, movements) = await ReadSavedAsync(account.Id, bag.Id);
            var answered = saved.Single(row => row.Id == suggestion.Id);
            Assert.Equal(answered.Status == SuggestionStatus.Accepted ? 1 : 0, movements.Count);
            Assert.NotEqual(SuggestionStatus.Proposed, answered.Status);
        }
        finally
        {
            await DeleteAccountAsync(email);
        }
    }
}
