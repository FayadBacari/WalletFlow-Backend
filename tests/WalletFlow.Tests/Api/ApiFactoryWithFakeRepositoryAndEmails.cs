using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Application.Emails;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Api;

// Lance la vraie API en mémoire pour les tests. Seuls la base et l'envoi des e-mails sont remplacés.
public class ApiFactoryWithFakeRepositoryAndEmails : WebApplicationFactory<Program>
{
    public FakeUserAccountRepository RepositoryThatKeepsAccountsInMemory { get; } = new FakeUserAccountRepository();
    public FakeEmailSender SenderThatKeepsTheEmailsInMemory { get; } = new FakeEmailSender();

    // Remplace les services avant le démarrage de l'API (nom imposé par le framework).
    protected override void ConfigureWebHost(IWebHostBuilder builderOfTheApiStartedForTheTests)
    {
        // Cette base n'est jamais contactée : le réglage évite seulement de dépendre des user-secrets.
        builderOfTheApiStartedForTheTests.UseSetting("ConnectionStrings:WalletFlowDatabase", "Host=localhost;Database=base_jamais_contactee");

        builderOfTheApiStartedForTheTests.ConfigureTestServices(servicesOfTheApiStartedForTheTests =>
        {
            servicesOfTheApiStartedForTheTests.AddSingleton<IUserAccountRepository>(RepositoryThatKeepsAccountsInMemory);
            servicesOfTheApiStartedForTheTests.AddSingleton<IEmailSender>(SenderThatKeepsTheEmailsInMemory);
            servicesOfTheApiStartedForTheTests.AddSingleton<IStartupFilter, StartupFilterThatReadsTheVisitorAddress>();
        });
    }
}
