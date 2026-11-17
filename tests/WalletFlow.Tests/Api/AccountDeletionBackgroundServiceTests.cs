using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Api.LoginRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la tâche horaire de la suppression (GR Suppression, SS §7) sur les services de la vraie API, avec une fausse base :
// les révocations en retard, puis l'effacement des comptes dont la suppression est demandée depuis 30 jours.
public class AccountDeletionBackgroundServiceTests
{
    // Crée la tâche à tester, branchée sur les services de cette API.
    private static AccountDeletionBackgroundService CreateTask(ApiFactoryWithFakes api)
    {
        return new AccountDeletionBackgroundService(
            api.Services.GetRequiredService<IServiceScopeFactory>(),
            api.Clock,
            NullLogger<AccountDeletionBackgroundService>.Instance);
    }

    // Ajoute une connexion bancaire active à ce compte.
    private static BankConnection AddConnection(ApiFactoryWithFakes api, Guid userId, string externalId)
    {
        var connection = new BankConnection
        {
            ExternalConnectionId = externalId,
            BankName = "Banque",
            AuthorizationExpiresAt = api.Clock.GetUtcNow().UtcDateTime.AddDays(90),
            UserId = userId
        };
        api.BankRepository.SavedConnections.Add(connection);

        return connection;
    }

    // Un passage efface le compte demandé il y a 30 jours, après avoir révoqué sa banque, et envoie l'e-mail d'effacement. Il
    // révoque la banque d'un compte encore dans ses 30 jours, sans l'effacer, et laisse les autres comptes tranquilles.
    [Fact]
    public async Task PassRevokesLateBanksThenErasesDueAccounts()
    {
        using var api = new ApiFactoryWithFakes();
        var now = api.Clock.GetUtcNow().UtcDateTime;
        var due = await AddAccountAsync(api, "a-effacer@exemple.test");
        due.RequestDeletion(now.AddDays(-30));
        var dueConnection = AddConnection(api, due.Id, "session-a-effacer");
        var waiting = await AddAccountAsync(api, "en-attente@exemple.test");
        waiting.RequestDeletion(now.AddDays(-5));
        var waitingConnection = AddConnection(api, waiting.Id, "session-en-attente");
        var active = await AddAccountAsync(api, "actif@exemple.test");
        var activeConnection = AddConnection(api, active.Id, "session-active");

        await CreateTask(api).RunPassAsync();

        Assert.DoesNotContain(due, api.AccountRepository.SavedUsers);
        Assert.Contains(waiting, api.AccountRepository.SavedUsers);
        Assert.Contains(active, api.AccountRepository.SavedUsers);
        Assert.Equal(new List<string> { "session-a-effacer", "session-en-attente" }, api.BankProvider.RevokedSessions.Order().ToList());
        Assert.Equal(
            (BankConnectionStatus.Expired, BankConnectionStatus.Expired, BankConnectionStatus.Active),
            (dueConnection.Status, waitingConnection.Status, activeConnection.Status));
        await RecoveryRequestTestValues.WaitUntilAsync(() => api.EmailSender.CountSentEmailsTo("a-effacer@exemple.test") == 1);
        Assert.Equal("Ton compte WalletFlow est effacé", api.EmailSender.FindLastSentEmailTo("a-effacer@exemple.test").Subject);
    }

    // SS §7 : un prestataire qui ne répond toujours pas n'empêche pas l'effacement ; l'autorisation finira d'elle-même.
    [Fact]
    public async Task ProviderStillDownDoesNotStopTheErasure()
    {
        using var api = new ApiFactoryWithFakes();
        var due = await AddAccountAsync(api, "a-effacer@exemple.test");
        due.RequestDeletion(api.Clock.GetUtcNow().UtcDateTime.AddDays(-31));
        AddConnection(api, due.Id, "session-a-effacer");
        api.BankProvider.RevocationFails = true;

        await CreateTask(api).RunPassAsync();

        Assert.DoesNotContain(due, api.AccountRepository.SavedUsers);
        Assert.Empty(api.BankProvider.RevokedSessions);
        await RecoveryRequestTestValues.WaitUntilAsync(() => api.EmailSender.CountSentEmailsTo("a-effacer@exemple.test") == 1);
        Assert.Equal("Ton compte WalletFlow est effacé", api.EmailSender.FindLastSentEmailTo("a-effacer@exemple.test").Subject);
    }
}
