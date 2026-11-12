using System.Net;
using WalletFlow.Application.BankConnections;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie la déconnexion d'une banque (GR Comptes, SS §4, §7 et §11), avec un faux prestataire.
public class BankDisconnectionServiceTests
{
    private static readonly IPAddress UserAddress = IPAddress.Parse("203.0.113.7");

    private readonly BankServices services = new BankServices();

    // Connecte la banque des tests avec un compte courant.
    private async Task<BankConnection> ConnectAsync()
    {
        return await services.ConnectAsync(new TestBankAccount("stable-1", "Compte courant", "EUR", 10_000));
    }

    // GR Comptes : l'autorisation est révoquée, puis la banque est supprimée avec ses comptes. Le journal le garde.
    [Fact]
    public async Task DisconnectionRevokesThenDeletes()
    {
        var connection = await ConnectAsync();

        var result = await services.CreateDisconnection().DisconnectAsync(services.UserId, connection.Id, UserAddress);

        Assert.IsType<BankDisconnectionResult.Disconnected>(result);
        Assert.Contains(connection.ExternalConnectionId, services.Provider.RevokedSessions);
        Assert.Empty(services.Repository.SavedConnections);
        Assert.Empty(services.Repository.SavedAccounts);
        var journalEvent = services.Journal.SavedSecurityEvents.Single(saved => saved.Type == SecurityEventType.BankDisconnection);
        Assert.Equal(SecurityEventResult.Success, journalEvent.Result);
        Assert.Equal(UserAddress, journalEvent.IpAddress);
    }

    // Si le prestataire ne confirme pas la révocation, rien n'est supprimé : l'utilisateur réessaiera.
    [Fact]
    public async Task UnconfirmedRevocationDeletesNothing()
    {
        var connection = await ConnectAsync();
        services.Provider.RevocationFails = true;

        var result = await services.CreateDisconnection().DisconnectAsync(services.UserId, connection.Id, UserAddress);

        Assert.IsType<BankDisconnectionResult.ProviderUnavailable>(result);
        Assert.Single(services.Repository.SavedConnections);
        Assert.Single(services.Repository.SavedAccounts);
    }

    // SS §4 : la connexion d'un autre utilisateur n'est pas trouvée, ni révoquée, ni supprimée.
    [Fact]
    public async Task ConnectionOfAnotherUserIsNotFound()
    {
        var connection = await ConnectAsync();

        var result = await services.CreateDisconnection().DisconnectAsync(Guid.NewGuid(), connection.Id, UserAddress);

        Assert.IsType<BankDisconnectionResult.NotFound>(result);
        Assert.Empty(services.Provider.RevokedSessions);
        Assert.Single(services.Repository.SavedConnections);
    }

    // Un renouvellement change la session entre sa révocation et la suppression : la nouvelle session est révoquée aussi,
    // et aucune session ne reste ouverte chez le prestataire.
    [Fact]
    public async Task SessionRenewedDuringTheDisconnectionIsRevokedToo()
    {
        var connection = await ConnectAsync();
        var oldSessionId = connection.ExternalConnectionId;
        services.Repository.RenewalBeforeDeletion = "session-renouvelee";

        var result = await services.CreateDisconnection().DisconnectAsync(services.UserId, connection.Id, UserAddress);

        Assert.IsType<BankDisconnectionResult.Disconnected>(result);
        Assert.Equal(new List<string> { oldSessionId, "session-renouvelee" }, services.Provider.RevokedSessions);
        Assert.Empty(services.Repository.SavedConnections);
    }

    // PostgreSQL arrête deux fois la suppression pour débloquer un interblocage : elle recommence, et finit par passer.
    [Fact]
    public async Task DeletionStoppedByADeadlockIsTriedAgain()
    {
        var connection = await ConnectAsync();
        services.Repository.StoppedDeletions = 2;

        var result = await services.CreateDisconnection().DisconnectAsync(services.UserId, connection.Id, UserAddress);

        Assert.IsType<BankDisconnectionResult.Disconnected>(result);
        Assert.Empty(services.Repository.SavedConnections);
    }

    // Si la suppression ne passe jamais, rien n'est supprimé, et l'utilisateur est invité à réessayer.
    [Fact]
    public async Task DeletionThatNeverPassesDeletesNothing()
    {
        var connection = await ConnectAsync();
        services.Repository.StoppedDeletions = BankDisconnectionService.MaximumTries;

        var result = await services.CreateDisconnection().DisconnectAsync(services.UserId, connection.Id, UserAddress);

        Assert.IsType<BankDisconnectionResult.NotSaved>(result);
        Assert.Single(services.Repository.SavedConnections);
        Assert.DoesNotContain(services.Journal.SavedSecurityEvents, saved => saved.Type == SecurityEventType.BankDisconnection);
    }

    [Fact]
    public async Task SecondDisconnectionIsNotFound()
    {
        var connection = await ConnectAsync();
        await services.CreateDisconnection().DisconnectAsync(services.UserId, connection.Id, UserAddress);

        var result = await services.CreateDisconnection().DisconnectAsync(services.UserId, connection.Id, UserAddress);

        Assert.IsType<BankDisconnectionResult.NotFound>(result);
    }
}
