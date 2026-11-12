using System.Net;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Infrastructure.BankData;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Crée les cas d'utilisation des connexions bancaires avec un faux prestataire et une fausse base.
// Les mémoires temporaires et le créateur de jetons sont les vrais : ils ont leurs propres tests.
public class BankServices
{
    public const string WebsiteAddress = "https://walletflow.test";

    public Guid UserId { get; } = Guid.NewGuid();
    public FakeTimeProvider Clock { get; } = new FakeTimeProvider();
    public FakeBankDataProvider Provider { get; } = new FakeBankDataProvider();
    public FakeBankConnectionRepository Repository { get; } = new FakeBankConnectionRepository();
    public FakeSecurityEventRepository Journal { get; } = new FakeSecurityEventRepository();
    public FakeDatabaseUnitOfWork UnitOfWork { get; }
    public InMemoryBankAuthorizationStore AuthorizationStore { get; }
    public InMemoryBankUpdateLimiter UpdateLimiter { get; }
    public InMemoryBankUpdateSchedule UpdateSchedule { get; }
    public RandomSecureTokenGenerator TokenGenerator { get; } = new RandomSecureTokenGenerator();
    public UserPresence Presence { get; } = new UserPresence(IPAddress.Parse("203.0.113.7"), "Navigateur de test");

    // Relie la fausse base et le faux journal à la fausse unité de travail.
    public BankServices()
    {
        UnitOfWork = new FakeDatabaseUnitOfWork(Repository, Journal);
        AuthorizationStore = new InMemoryBankAuthorizationStore(Clock);
        UpdateLimiter = new InMemoryBankUpdateLimiter(Clock);
        UpdateSchedule = new InMemoryBankUpdateSchedule(Clock);
        Provider.SessionValidUntil = Now + BankConnectionRules.MaximumAuthorizationDuration - BankConnectionRules.ClockMargin;
    }

    // L'heure de l'horloge des tests.
    public DateTime Now => Clock.GetUtcNow().UtcDateTime;

    // Crée le service qui commence une autorisation.
    public BankAuthorizationStartService CreateStart()
    {
        return new BankAuthorizationStartService(Provider, Repository, AuthorizationStore, TokenGenerator, new PublicWebsiteSettings(WebsiteAddress), Clock);
    }

    // Crée le service qui termine une autorisation.
    public BankConnectionFinishService CreateFinish()
    {
        return new BankConnectionFinishService(Provider, Repository, AuthorizationStore, TokenGenerator, CreateSavingService(), CreateJournal(), Clock);
    }

    // Crée le service qui met à jour les soldes.
    public BankBalanceUpdateService CreateUpdate()
    {
        return new BankBalanceUpdateService(Provider, Repository, UpdateLimiter, CreateSavingService(), Clock);
    }

    // Crée le service de la mise à jour en arrière-plan.
    public BankBackgroundUpdateService CreateBackgroundUpdate()
    {
        return new BankBackgroundUpdateService(Repository, UpdateSchedule, CreateUpdate());
    }

    // Crée le service qui déconnecte une banque.
    public BankDisconnectionService CreateDisconnection()
    {
        return new BankDisconnectionService(Provider, Repository, CreateJournal());
    }

    // Crée le service qui liste les connexions.
    public BankConnectionListService CreateConnectionList()
    {
        return new BankConnectionListService(Repository, Clock);
    }

    // Commence une autorisation pour cette banque, et renvoie le state que le faux prestataire a reçu.
    public async Task<string> StartAsync(string bankName = FakeBankDataProvider.BankName, Guid? userId = null)
    {
        var result = await CreateStart().StartNewAsync(userId ?? UserId, bankName);
        Assert.IsType<BankAuthorizationStartResult.Started>(result);

        return Provider.AuthorizationRequests.Last().State;
    }

    // Connecte une banque avec ces comptes, et renvoie la connexion gardée.
    public async Task<BankConnection> ConnectAsync(params TestBankAccount[] accounts)
    {
        var state = await StartAsync();
        var code = "code-" + Guid.NewGuid().ToString("N");
        Provider.AddSession(code, accounts);

        var result = await CreateFinish().FinishAsync(UserId, code, state, Presence);
        var connected = Assert.IsType<BankConnectionFinishResult.Connected>(result);

        return Repository.SavedConnections.Single(connection => connection.Id == connected.ConnectionId);
    }

    // Donne les comptes gardés pour cette connexion.
    public List<BankAccount> AccountsOf(BankConnection connection)
    {
        return Repository.SavedAccounts.Where(account => account.BankConnectionId == connection.Id).ToList();
    }

    // Crée l'enregistrement avec réessai, sur la fausse unité de travail.
    private DatabaseChangesSavingService CreateSavingService()
    {
        return new DatabaseChangesSavingService(UnitOfWork);
    }

    // Crée le journal de sécurité, sur le faux journal.
    private SecurityJournalService CreateJournal()
    {
        return new SecurityJournalService(Journal, Clock);
    }
}
