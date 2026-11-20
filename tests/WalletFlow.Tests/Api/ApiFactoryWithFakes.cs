using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WalletFlow.Application.Administration;
using WalletFlow.Application.Bags;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Categories;
using WalletFlow.Application.DataExport;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Security;
using WalletFlow.Application.Suggestions;
using WalletFlow.Application.Support;
using WalletFlow.Application.Transactions;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Application.Notifications;
using WalletFlow.Application.Persistence;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Api;

// Lance la vraie API en mémoire pour les tests. Seuls la base, l'envoi des e-mails et l'horloge sont remplacés.
public class ApiFactoryWithFakes : WebApplicationFactory<Program>
{
    public FakeUserAccountRepository AccountRepository { get; } = new FakeUserAccountRepository();
    public FakeSecurityEventRepository SecurityEventRepository { get; } = new FakeSecurityEventRepository();
    public FakeUserSessionRepository SessionRepository { get; } = new FakeUserSessionRepository();
    public FakeTwoFactorAuthenticationRepository TwoFactorRepository { get; } = new FakeTwoFactorAuthenticationRepository();
    public FakeCategoryRepository CategoryRepository { get; }
    public FakeBankConnectionRepository BankRepository { get; } = new FakeBankConnectionRepository();
    public FakeTransactionRepository TransactionRepository { get; }
    public FakeBagRepository BagRepository { get; }
    public FakeSuggestionRepository SuggestionRepository { get; }
    public FakeResourceOwnershipRepository OwnershipRepository { get; }
    public EmailRequestWakeUp EmailWakeUp { get; } = new EmailRequestWakeUp();
    public FakeEmailRequestRepository EmailRequests { get; }
    public FakeAlertRepository AlertRepository { get; } = new FakeAlertRepository();
    public FakeSupportRequestRepository SupportRepository { get; }
    public FakeAdministrationRepository AdministrationRepository { get; }
    public FakeDataExportRepository ExportRepository { get; } = new FakeDataExportRepository();
    public FakeBankDataProvider BankProvider { get; } = new FakeBankDataProvider();
    public FakeDatabaseUnitOfWork UnitOfWork { get; }
    public FakeDatabaseHealth DatabaseHealth { get; } = new FakeDatabaseHealth();
    public FakeEmailSender EmailSender { get; } = new FakeEmailSender();
    public FakeBreachedPasswordChecker BreachedPasswordChecker { get; } = new FakeBreachedPasswordChecker();
    public FakeTimeProvider Clock { get; } = new FakeTimeProvider();
    public MemoryLoggerProvider LoggerProvider { get; } = new MemoryLoggerProvider();

    // La clé qui chiffre les secrets : les tests n'ont pas besoin de la connaître à l'avance, et aucune clé n'entre dans Git.
    public string TotpEncryptionKey { get; } = TwoFactorTestTools.CreateEncryptionKey();

    // Relie les faux accès à la fausse unité de travail, qui garde leurs changements ensemble.
    // Les catégories créées à l'inscription sont celles que le faux accès aux catégories lit, et les comptes inscrits
    // sont ceux que le faux accès aux sacs lit.
    public ApiFactoryWithFakes()
    {
        CategoryRepository = new FakeCategoryRepository(AccountRepository.SavedCategories);
        TransactionRepository = new FakeTransactionRepository(BankRepository, CategoryRepository);
        BagRepository = new FakeBagRepository(AccountRepository.SavedUsers);
        SuggestionRepository = new FakeSuggestionRepository(BagRepository) { Transactions = TransactionRepository };
        AlertRepository.Categories = CategoryRepository;
        AlertRepository.Bags = BagRepository;
        AlertRepository.Connections = BankRepository;
        BankRepository.Users = AccountRepository.SavedUsers;
        SupportRepository = new FakeSupportRequestRepository(AccountRepository.SavedUsers);
        AdministrationRepository = new FakeAdministrationRepository(SupportRepository, BankRepository);
        OwnershipRepository = new FakeResourceOwnershipRepository(
            CategoryRepository, BankRepository, BagRepository, SuggestionRepository, SupportRepository);
        EmailRequests = new FakeEmailRequestRepository(EmailWakeUp);
        UnitOfWork = new FakeDatabaseUnitOfWork(
            AccountRepository,
            SessionRepository,
            TwoFactorRepository,
            SecurityEventRepository,
            CategoryRepository,
            BankRepository,
            TransactionRepository,
            BagRepository,
            SuggestionRepository,
            EmailRequests,
            AlertRepository,
            SupportRepository,
            AdministrationRepository);
    }

    // Remplace les services avant le démarrage de l'API (nom imposé par le framework).
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestApiSettings.Apply(builder, TotpEncryptionKey);

        builder.ConfigureTestServices(services =>
        {
            // Comme un DbContext par portée, chaque requête et chaque étape d'une tâche de fond reçoit ses propres vues des faux
            // dépôts, avec leur unité de travail : ses changements en attente ne se mêlent jamais à ceux d'une autre, sur la même
            // fausse base. Les propriétés de cette fabrique sont les premières vues, où les tests lisent et remplissent la base.
            services.AddScoped(serviceProvider => new ScopeViews(this));
            services.AddScoped<IDatabaseUnitOfWork>(serviceProvider => serviceProvider.GetRequiredService<ScopeViews>().UnitOfWork);
            services.AddScoped<IUserAccountRepository>(serviceProvider => serviceProvider.GetRequiredService<ScopeViews>().Accounts);
            services.AddScoped<IUserSessionRepository>(serviceProvider => serviceProvider.GetRequiredService<ScopeViews>().Sessions);
            services.AddScoped<ISecurityEventRepository>(serviceProvider => serviceProvider.GetRequiredService<ScopeViews>().SecurityEvents);
            services.AddScoped<ITwoFactorAuthenticationRepository>(serviceProvider => serviceProvider.GetRequiredService<ScopeViews>().TwoFactor);
            services.AddScoped<ICategoryRepository>(serviceProvider => serviceProvider.GetRequiredService<ScopeViews>().Categories);
            services.AddScoped<IBankConnectionRepository>(serviceProvider => serviceProvider.GetRequiredService<ScopeViews>().Banks);
            services.AddScoped<ITransactionRepository>(serviceProvider => serviceProvider.GetRequiredService<ScopeViews>().Transactions);
            services.AddScoped<IBagRepository>(serviceProvider => serviceProvider.GetRequiredService<ScopeViews>().Bags);
            services.AddScoped<ISuggestionRepository>(serviceProvider => serviceProvider.GetRequiredService<ScopeViews>().Suggestions);
            services.AddScoped<IEmailRequestRepository>(serviceProvider => serviceProvider.GetRequiredService<ScopeViews>().EmailRequests);
            services.AddScoped<IAlertRepository>(serviceProvider => serviceProvider.GetRequiredService<ScopeViews>().Alerts);
            services.AddScoped<ISupportRequestRepository>(serviceProvider => serviceProvider.GetRequiredService<ScopeViews>().Support);
            services.AddScoped<IAdministrationRepository>(serviceProvider => serviceProvider.GetRequiredService<ScopeViews>().Administration);
            services.AddSingleton<IDatabaseHealth>(DatabaseHealth);
            services.AddSingleton<IResourceOwnershipRepository>(OwnershipRepository);
            services.AddSingleton<IDataExportRepository>(ExportRepository);
            services.AddSingleton(EmailWakeUp);
            services.AddSingleton<IBankDataProvider>(BankProvider);
            services.AddSingleton<IEmailSender>(EmailSender);
            services.AddSingleton<IBreachedPasswordChecker>(BreachedPasswordChecker);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<ILoggerProvider>(LoggerProvider);
            services.AddSingleton<IStartupFilter, VisitorAddressStartupFilter>();
            ResponseContractCheck.AddTo(services);
        });
    }

    // Les vues des faux dépôts d'une portée de services, et leur unité de travail, dans le même ordre que celle de la fabrique.
    private sealed class ScopeViews
    {
        // Crée une vue de chaque faux dépôt de la fabrique, et l'unité de travail qui les enregistre ensemble.
        public ScopeViews(ApiFactoryWithFakes api)
        {
            Accounts = api.AccountRepository.CreateView();
            Sessions = api.SessionRepository.CreateView();
            TwoFactor = api.TwoFactorRepository.CreateView();
            SecurityEvents = api.SecurityEventRepository.CreateView();
            Categories = api.CategoryRepository.CreateView();
            Banks = api.BankRepository.CreateView();
            Transactions = api.TransactionRepository.CreateView();
            Bags = api.BagRepository.CreateView();
            Suggestions = api.SuggestionRepository.CreateView();
            EmailRequests = api.EmailRequests.CreateView();
            Alerts = api.AlertRepository.CreateView();
            Support = api.SupportRepository.CreateView();
            Administration = api.AdministrationRepository.CreateView();
            UnitOfWork = api.UnitOfWork.CreateView(
                Accounts,
                Sessions,
                TwoFactor,
                SecurityEvents,
                Categories,
                Banks,
                Transactions,
                Bags,
                Suggestions,
                EmailRequests,
                Alerts,
                Support,
                Administration);
        }

        public FakeUserAccountRepository Accounts { get; }

        public FakeUserSessionRepository Sessions { get; }

        public FakeTwoFactorAuthenticationRepository TwoFactor { get; }

        public FakeSecurityEventRepository SecurityEvents { get; }

        public FakeCategoryRepository Categories { get; }

        public FakeBankConnectionRepository Banks { get; }

        public FakeTransactionRepository Transactions { get; }

        public FakeBagRepository Bags { get; }

        public FakeSuggestionRepository Suggestions { get; }

        public FakeEmailRequestRepository EmailRequests { get; }

        public FakeAlertRepository Alerts { get; }

        public FakeSupportRequestRepository Support { get; }

        public FakeAdministrationRepository Administration { get; }

        public FakeDatabaseUnitOfWork UnitOfWork { get; }
    }
}
