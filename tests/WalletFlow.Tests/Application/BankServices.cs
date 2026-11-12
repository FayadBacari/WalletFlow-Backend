using System.Net;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.Transactions;
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
    public FakeCategoryRepository Categories { get; } = new FakeCategoryRepository();
    public FakeTransactionRepository Transactions { get; }
    public FakeSecurityEventRepository Journal { get; } = new FakeSecurityEventRepository();
    public FakeDatabaseUnitOfWork UnitOfWork { get; }
    public InMemoryBankAuthorizationStore AuthorizationStore { get; }
    public InMemoryBankUpdateLimiter UpdateLimiter { get; }
    public InMemoryBankUpdateSchedule UpdateSchedule { get; }
    public RandomSecureTokenGenerator TokenGenerator { get; } = new RandomSecureTokenGenerator();
    public UserPresence Presence { get; } = new UserPresence(IPAddress.Parse("203.0.113.7"), "Navigateur de test");

    // Relie la fausse base et le faux journal à la fausse unité de travail. L'utilisateur des tests a ses 8 catégories par défaut.
    public BankServices()
    {
        Transactions = new FakeTransactionRepository(Repository, Categories);
        UnitOfWork = new FakeDatabaseUnitOfWork(Repository, Journal, Categories, Transactions);
        Categories.SavedCategories.AddRange(DefaultCategories.CreateForUser(UserId));
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
        return new BankConnectionFinishService(Provider, Repository, AuthorizationStore, TokenGenerator, CreateArrival(), CreateSavingService(), CreateJournal(), Clock);
    }

    // Crée le service qui met à jour les soldes.
    public BankBalanceUpdateService CreateUpdate()
    {
        return new BankBalanceUpdateService(Provider, Repository, UpdateLimiter, CreateArrival(), CreateSavingService(), Clock);
    }

    // Crée le service qui change la catégorie d'une opération.
    public TransactionCategoryChangeService CreateCategoryChange()
    {
        return new TransactionCategoryChangeService(Transactions, Categories, CreateSavingService());
    }

    // Crée le service qui marque une opération comme transfert.
    public TransactionTransferService CreateTransfer()
    {
        return new TransactionTransferService(Transactions, CreateSavingService());
    }

    // Crée le service qui cherche les opérations.
    public TransactionSearchService CreateSearch()
    {
        return new TransactionSearchService(Transactions);
    }

    // Crée le service du solde total.
    public BankBalanceTotalService CreateBalanceTotal()
    {
        return new BankBalanceTotalService(Repository);
    }

    // Donne la catégorie de l'utilisateur des tests qui porte ce nom.
    public Category CategoryNamed(string name)
    {
        return Categories.SavedCategories.Single(category => category.UserId == UserId && category.Name == name);
    }

    // Donne les opérations gardées pour ce compte, les plus anciennes d'abord.
    public List<BankTransaction> TransactionsOf(BankAccount account)
    {
        return Transactions.SavedTransactions
            .Where(transaction => transaction.BankAccountId == account.Id)
            .OrderBy(transaction => transaction.TransactionDate)
            .ToList();
    }

    // Le jour de l'horloge des tests.
    public DateOnly Today => DateOnly.FromDateTime(Now);

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

    // Crée l'arrivée des opérations, sur les fausses opérations et les fausses catégories.
    private BankTransactionArrival CreateArrival()
    {
        return new BankTransactionArrival(Transactions, Categories);
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
