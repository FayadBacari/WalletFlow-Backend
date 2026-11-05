using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Security;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Api;

// Lance la vraie API en mémoire pour les tests. Seuls la base, l'envoi des e-mails et l'horloge sont remplacés.
public class ApiFactoryWithFakeRepositoryAndEmails : WebApplicationFactory<Program>
{
    public FakeUserAccountRepository RepositoryThatKeepsAccountsInMemory { get; } = new FakeUserAccountRepository();
    public FakeSecurityEventRepository RepositoryThatKeepsSecurityEventsInMemory { get; } = new FakeSecurityEventRepository();
    public FakeUserSessionRepository RepositoryThatKeepsSessionsInMemory { get; }
    public FakeTwoFactorAuthenticationRepository RepositoryThatKeepsTwoFactorDataInMemory { get; }
    public FakeEmailSender SenderThatKeepsTheEmailsInMemory { get; } = new FakeEmailSender();
    public FakeTimeProviderForTests ClockThatOnlyMovesWhenTestsAskIt { get; } = new FakeTimeProviderForTests();
    public LoggerProviderThatKeepsTheMessages LoggerThatKeepsTheMessagesInMemory { get; } = new LoggerProviderThatKeepsTheMessages();

    // La clé qui chiffre les secrets : les tests n'ont pas besoin de la connaître à l'avance, et aucune clé n'entre dans Git.
    public string EncryptionKeyOfTheTotpSecretsInTheseTests { get; } = TwoFactorTestTools.CreateNewEncryptionKeyForOneTest();

    // Relie les faux accès aux sessions et à la double authentification au faux journal de sécurité.
    public ApiFactoryWithFakeRepositoryAndEmails()
    {
        RepositoryThatKeepsSessionsInMemory = new FakeUserSessionRepository(RepositoryThatKeepsSecurityEventsInMemory);
        RepositoryThatKeepsTwoFactorDataInMemory = new FakeTwoFactorAuthenticationRepository(RepositoryThatKeepsAccountsInMemory, RepositoryThatKeepsSecurityEventsInMemory);
    }

    // Remplace les services avant le démarrage de l'API (nom imposé par le framework).
    protected override void ConfigureWebHost(IWebHostBuilder builderOfTheApiStartedForTheTests)
    {
        // Cette base n'est jamais contactée : le réglage évite seulement de dépendre des user-secrets.
        builderOfTheApiStartedForTheTests.UseSetting("ConnectionStrings:WalletFlowDatabase", "Host=localhost;Database=base_jamais_contactee");
        builderOfTheApiStartedForTheTests.UseSetting("TwoFactorSecretEncryptionKey", EncryptionKeyOfTheTotpSecretsInTheseTests);

        builderOfTheApiStartedForTheTests.ConfigureTestServices(servicesOfTheApiStartedForTheTests =>
        {
            servicesOfTheApiStartedForTheTests.AddSingleton<IUserAccountRepository>(RepositoryThatKeepsAccountsInMemory);
            servicesOfTheApiStartedForTheTests.AddSingleton<IUserSessionRepository>(RepositoryThatKeepsSessionsInMemory);
            servicesOfTheApiStartedForTheTests.AddSingleton<ISecurityEventRepository>(RepositoryThatKeepsSecurityEventsInMemory);
            servicesOfTheApiStartedForTheTests.AddSingleton<ITwoFactorAuthenticationRepository>(RepositoryThatKeepsTwoFactorDataInMemory);
            servicesOfTheApiStartedForTheTests.AddSingleton<IEmailSender>(SenderThatKeepsTheEmailsInMemory);
            servicesOfTheApiStartedForTheTests.AddSingleton<TimeProvider>(ClockThatOnlyMovesWhenTestsAskIt);
            servicesOfTheApiStartedForTheTests.AddSingleton<ILoggerProvider>(LoggerThatKeepsTheMessagesInMemory);
            servicesOfTheApiStartedForTheTests.AddSingleton<IStartupFilter, StartupFilterThatReadsTheVisitorAddress>();
        });
    }
}
