using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Categories;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Security;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
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
    public FakeBankDataProvider BankProvider { get; } = new FakeBankDataProvider();
    public FakeDatabaseUnitOfWork UnitOfWork { get; }
    public FakeEmailSender EmailSender { get; } = new FakeEmailSender();
    public FakeTimeProvider Clock { get; } = new FakeTimeProvider();
    public MemoryLoggerProvider LoggerProvider { get; } = new MemoryLoggerProvider();

    // La clé qui chiffre les secrets : les tests n'ont pas besoin de la connaître à l'avance, et aucune clé n'entre dans Git.
    public string TotpEncryptionKey { get; } = TwoFactorTestTools.CreateEncryptionKey();

    // Relie les faux accès à la fausse unité de travail, qui garde leurs changements ensemble.
    // Les catégories créées à l'inscription sont celles que le faux accès aux catégories lit.
    public ApiFactoryWithFakes()
    {
        CategoryRepository = new FakeCategoryRepository(AccountRepository.SavedCategories);
        UnitOfWork = new FakeDatabaseUnitOfWork(
            AccountRepository,
            SessionRepository,
            TwoFactorRepository,
            SecurityEventRepository,
            CategoryRepository,
            BankRepository);
    }

    // Remplace les services avant le démarrage de l'API (nom imposé par le framework).
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestApiSettings.Apply(builder, TotpEncryptionKey);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IDatabaseUnitOfWork>(UnitOfWork);
            services.AddSingleton<IUserAccountRepository>(AccountRepository);
            services.AddSingleton<IUserSessionRepository>(SessionRepository);
            services.AddSingleton<ISecurityEventRepository>(SecurityEventRepository);
            services.AddSingleton<ITwoFactorAuthenticationRepository>(TwoFactorRepository);
            services.AddSingleton<ICategoryRepository>(CategoryRepository);
            services.AddSingleton<IBankConnectionRepository>(BankRepository);
            services.AddSingleton<IBankDataProvider>(BankProvider);
            services.AddSingleton<IEmailSender>(EmailSender);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<ILoggerProvider>(LoggerProvider);
            services.AddSingleton<IStartupFilter, VisitorAddressStartupFilter>();
        });
    }
}
