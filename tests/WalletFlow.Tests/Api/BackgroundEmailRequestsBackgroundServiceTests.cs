using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Api;

// Vérifie la tâche de fond qui prépare les e-mails de récupération et de déblocage.
public class BackgroundEmailRequestsBackgroundServiceTests
{
    private const string AccountEmail = "utilisateur@exemple.test";

    private readonly FakeUserAccountRepository accountRepository = new FakeUserAccountRepository();
    private readonly FakeEmailSender emailSender = new FakeEmailSender();
    private readonly MemoryLoggerProvider loggerProvider = new MemoryLoggerProvider();

    // Crée la tâche à tester, branchée sur ces faux outils.
    private BackgroundEmailRequestsBackgroundService CreateBackgroundTask()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUserAccountRepository>(accountRepository);
        services.AddSingleton<ISecurityEventRepository>(new FakeSecurityEventRepository());
        services.AddSingleton<ISecureTokenGenerator>(new FakeSecureTokenGenerator());
        services.AddSingleton<IEmailSender>(emailSender);
        services.AddEmailTexts();
        services.AddSingleton(new PublicWebsiteSettings("https://walletflow.test"));
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddScoped<SecurityJournalService>();
        services.AddScoped<BackgroundEmailRequestProcessingService>();
        var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = new LoggerFactory(new ILoggerProvider[] { loggerProvider });

        return new BackgroundEmailRequestsBackgroundService(
            new FakeBackgroundEmailRequestQueue(),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new Logger<BackgroundEmailRequestsBackgroundService>(loggerFactory));
    }

    // Crée une demande de récupération pour l'adresse de ces tests.
    private static BackgroundEmailRequest CreateRecoveryRequest()
    {
        return new BackgroundEmailRequest.PasswordRecovery
        {
            EmailAddress = AccountEmail
        };
    }

    [Fact]
    public async Task OneRequestSendsItsEmailWithoutWritingAnyError()
    {
        accountRepository.SavedUsers.Add(new User { Email = AccountEmail, IsEmailConfirmed = true });

        await CreateBackgroundTask().ProcessNextRequestAsync(CreateRecoveryRequest());

        Assert.Single(emailSender.SentEmails);
        Assert.DoesNotContain(loggerProvider.Messages, message => message.StartsWith("Error"));
    }

    // Sans cette trace, personne ne saurait que les e-mails ne partent plus. L'utilisateur est désigné par son identifiant (SS §7).
    [Fact]
    public async Task EmailFailureIsWrittenWithTheUserAndWithoutTheAddress()
    {
        var account = new User { Email = AccountEmail, IsEmailConfirmed = true };
        accountRepository.SavedUsers.Add(account);
        emailSender.AlwaysFails = true;

        await CreateBackgroundTask().ProcessNextRequestAsync(CreateRecoveryRequest());

        var message = Assert.Single(loggerProvider.Messages, message => message.StartsWith("Error"));
        Assert.Contains("n'a pas pu partir", message);
        Assert.Contains("PasswordRecovery", message);
        Assert.Contains(account.Id.ToString(), message);
        Assert.DoesNotContain(AccountEmail, message);
    }

    // Une panne ne doit pas arrêter la tâche : les demandes suivantes doivent encore partir.
    [Fact]
    public async Task DatabaseFailureIsWrittenAndDoesNotStopTheTask()
    {
        accountRepository.SavedUsers.Add(new User { Email = AccountEmail, IsEmailConfirmed = true });
        accountRepository.IsDown = true;
        var backgroundTask = CreateBackgroundTask();

        await backgroundTask.ProcessNextRequestAsync(CreateRecoveryRequest());

        Assert.Contains(loggerProvider.Messages, message =>
            message.StartsWith("Error") && message.Contains("a échoué"));
        Assert.Empty(emailSender.SentEmails);

        accountRepository.IsDown = false;
        await backgroundTask.ProcessNextRequestAsync(CreateRecoveryRequest());

        Assert.Single(emailSender.SentEmails);
    }
}
