using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Application.Emails;
using WalletFlow.Tests.Api;
using WalletFlow.Tests.Fakes;
using WalletFlow.Tests.Infrastructure;

namespace WalletFlow.Tests.EndToEnd;

// Lance la vraie API sur la base de test, avec tout son branchement : injection des dépendances, unité de travail, dépôts,
// EF Core et tâches de fond. Seul l'envoi des e-mails est remplacé, pour que le test lise les liens.
// Ces tests écrivent pour de vrai dans la base de test : chacun efface lui-même son compte à la fin.
public class ApiFactoryWithRealDatabase : WebApplicationFactory<Program>
{
    public FakeEmailSender EmailSender { get; } = new FakeEmailSender();

    // Branche l'API sur la base de test avant son démarrage (nom imposé par le framework).
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestApiSettings.Apply(builder, TwoFactorTestTools.CreateEncryptionKey());
        builder.UseSetting("ConnectionStrings:WalletFlowDatabase", LocalTestDatabase.ReadConnectionString());

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IEmailSender>(EmailSender);
        });
    }
}
