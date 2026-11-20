using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Application.Administration;
using WalletFlow.Application.Bags;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Categories;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Notifications;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.Suggestions;
using WalletFlow.Application.Support;
using WalletFlow.Application.Transactions;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Api;

namespace WalletFlow.Tests.Fakes;

// Vérifie que chaque vue d'un faux dépôt a ses propres changements en attente, comme un DbContext, sur la même fausse base :
// l'enregistrement ou l'annulation d'une transaction ne touche jamais ce qu'une autre prépare. Avant les vues, une tâche de fond
// pouvait enregistrer, ou effacer, ce qu'une requête n'avait pas fini de préparer.
public class FakeRepositoryViewsTests
{
    // Crée un événement du journal pour un compte quelconque.
    private static SecurityEvent CreateEvent()
    {
        return new SecurityEvent
        {
            Type = SecurityEventType.Login,
            Result = SecurityEventResult.Success,
            OccurredAt = DateTime.UtcNow,
            UserId = Guid.NewGuid()
        };
    }

    // L'enregistrement d'une transaction n'emporte pas ce qu'une autre a préparé ; celle-ci l'enregistre ensuite elle-même.
    [Fact]
    public async Task CommitOfOneViewLeavesWhatAnotherPrepared()
    {
        var journal = new FakeSecurityEventRepository();
        var unitOfWork = new FakeDatabaseUnitOfWork(journal);
        var first = journal.CreateView();
        var second = journal.CreateView();
        var firstEvent = CreateEvent();
        var secondEvent = CreateEvent();
        first.PrepareEvent(firstEvent);
        second.PrepareEvent(secondEvent);

        Assert.True(await unitOfWork.CreateView(second).TrySaveChangesAsync());
        var savedAfterSecond = journal.SavedSecurityEvents.ToList();
        Assert.True(await unitOfWork.CreateView(first).TrySaveChangesAsync());

        Assert.Equal(new List<SecurityEvent> { secondEvent }, savedAfterSecond);
        Assert.Equal(new List<SecurityEvent> { secondEvent, firstEvent }, journal.SavedSecurityEvents);
        Assert.Equal(2, unitOfWork.CommittedTransactionCount);
    }

    // L'annulation d'une transaction, sur un conflit simulé, n'efface pas ce qu'une autre a préparé.
    [Fact]
    public async Task RollbackOfOneViewLeavesWhatAnotherPrepared()
    {
        var categories = new FakeCategoryRepository { Conflicts = 1 };
        var unitOfWork = new FakeDatabaseUnitOfWork(categories);
        var first = categories.CreateView();
        var second = categories.CreateView();
        var userId = Guid.NewGuid();
        var keptCategory = new Category { Name = "Gardée", UserId = userId };
        first.PrepareCategory(keptCategory);
        second.PrepareCategory(new Category { Name = "Annulée", UserId = userId });

        var savedBySecond = await unitOfWork.CreateView(second).TrySaveChangesAsync();
        var savedByFirst = await unitOfWork.CreateView(first).TrySaveChangesAsync();

        Assert.False(savedBySecond);
        Assert.True(savedByFirst);
        Assert.Equal(new List<Category> { keptCategory }, categories.SavedCategories);
    }

    // Dans une portée de l'API, chacun des 13 faux dépôts est une vue distincte de celle de la fabrique, sur la même fausse base :
    // ses données enregistrées sont les mêmes listes.
    [Fact]
    public void EveryRepositoryOfAScopeIsAViewOnTheSameDatabase()
    {
        using var api = new ApiFactoryWithFakes();
        using var scope = api.Services.CreateScope();
        var services = scope.ServiceProvider;
        var rootsAndViews = new List<(object Root, object View)>
        {
            (api.AccountRepository, services.GetRequiredService<IUserAccountRepository>()),
            (api.SessionRepository, services.GetRequiredService<IUserSessionRepository>()),
            (api.TwoFactorRepository, services.GetRequiredService<ITwoFactorAuthenticationRepository>()),
            (api.SecurityEventRepository, services.GetRequiredService<ISecurityEventRepository>()),
            (api.CategoryRepository, services.GetRequiredService<ICategoryRepository>()),
            (api.BankRepository, services.GetRequiredService<IBankConnectionRepository>()),
            (api.TransactionRepository, services.GetRequiredService<ITransactionRepository>()),
            (api.BagRepository, services.GetRequiredService<IBagRepository>()),
            (api.SuggestionRepository, services.GetRequiredService<ISuggestionRepository>()),
            (api.EmailRequests, services.GetRequiredService<IEmailRequestRepository>()),
            (api.AlertRepository, services.GetRequiredService<IAlertRepository>()),
            (api.SupportRepository, services.GetRequiredService<ISupportRequestRepository>()),
            (api.AdministrationRepository, services.GetRequiredService<IAdministrationRepository>())
        };
        var sharedListCount = 0;

        foreach (var (root, view) in rootsAndViews)
        {
            Assert.NotSame(root, view);
            Assert.IsType(root.GetType(), view);
            foreach (var property in root.GetType().GetProperties().Where(property =>
                property.Name.StartsWith("Saved", StringComparison.Ordinal)
                && property.PropertyType.IsGenericType
                && property.PropertyType.GetGenericTypeDefinition() == typeof(List<>)))
            {
                Assert.Same(property.GetValue(root), property.GetValue(view));
                sharedListCount = sharedListCount + 1;
            }
        }

        Assert.True(sharedListCount >= 25, "Seules " + sharedListCount + " listes enregistrées ont été comparées.");
    }

    // Dans une API de test, chaque portée de services a ses propres vues et sa propre unité de travail, et une même portée garde
    // les mêmes : ce qu'une requête prépare n'est enregistré que par son unité de travail.
    [Fact]
    public async Task EachServiceScopeOfTheApiHasItsOwnViews()
    {
        using var api = new ApiFactoryWithFakes();
        using var firstScope = api.Services.CreateScope();
        using var secondScope = api.Services.CreateScope();
        var firstJournal = firstScope.ServiceProvider.GetRequiredService<ISecurityEventRepository>();
        var secondJournal = secondScope.ServiceProvider.GetRequiredService<ISecurityEventRepository>();
        var securityEvent = CreateEvent();

        firstJournal.PrepareEvent(securityEvent);
        var savedBySecond = await secondScope.ServiceProvider.GetRequiredService<IDatabaseUnitOfWork>().TrySaveChangesAsync();
        var keptAfterSecond = api.SecurityEventRepository.SavedSecurityEvents.Contains(securityEvent);
        var savedByFirst = await firstScope.ServiceProvider.GetRequiredService<IDatabaseUnitOfWork>().TrySaveChangesAsync();

        Assert.NotSame(firstJournal, secondJournal);
        Assert.NotSame(api.SecurityEventRepository, firstJournal);
        Assert.Same(firstJournal, firstScope.ServiceProvider.GetRequiredService<ISecurityEventRepository>());
        Assert.True(savedBySecond);
        Assert.True(savedByFirst);
        Assert.False(keptAfterSecond);
        Assert.Contains(securityEvent, api.SecurityEventRepository.SavedSecurityEvents);
    }
}
