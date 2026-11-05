using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Api;

// Vérifie la tâche de fond qui prépare les e-mails de récupération et de déblocage.
public class BackgroundEmailRequestsBackgroundServiceTests
{
    private const string EmailAddressOfTheAccountInTheseTests = "utilisateur@exemple.test";

    private readonly FakeUserAccountRepository repositoryThatKeepsAccountsInMemory = new FakeUserAccountRepository();
    private readonly FakeEmailSender senderThatKeepsTheEmailsInMemory = new FakeEmailSender();
    private readonly LoggerProviderThatKeepsTheMessages providerThatKeepsTheLogMessages = new LoggerProviderThatKeepsTheMessages();

    // Crée la tâche à tester, branchée sur ces faux outils.
    private BackgroundEmailRequestsBackgroundService CreateBackgroundTaskWithFakeTools()
    {
        var servicesUsedByTheBackgroundTask = new ServiceCollection();
        servicesUsedByTheBackgroundTask.AddSingleton<IUserAccountRepository>(repositoryThatKeepsAccountsInMemory);
        servicesUsedByTheBackgroundTask.AddSingleton<ISecurityEventRepository>(new FakeSecurityEventRepository());
        servicesUsedByTheBackgroundTask.AddSingleton<ISecureTokenGenerator>(new FakeSecureTokenGenerator());
        servicesUsedByTheBackgroundTask.AddSingleton<IEmailSender>(senderThatKeepsTheEmailsInMemory);
        servicesUsedByTheBackgroundTask.AddSingleton(new PublicWebsiteSettings("https://walletflow.test"));
        servicesUsedByTheBackgroundTask.AddSingleton<TimeProvider>(new FakeTimeProviderForTests());
        servicesUsedByTheBackgroundTask.AddScoped<BackgroundEmailRequestProcessingService>();
        var providerOfTheBackgroundTaskServices = servicesUsedByTheBackgroundTask.BuildServiceProvider();
        var factoryOfTheLoggersOfThisTest = new LoggerFactory(new ILoggerProvider[] { providerThatKeepsTheLogMessages });

        return new BackgroundEmailRequestsBackgroundService(
            new FakeBackgroundEmailRequestQueue(),
            providerOfTheBackgroundTaskServices.GetRequiredService<IServiceScopeFactory>(),
            new Logger<BackgroundEmailRequestsBackgroundService>(factoryOfTheLoggersOfThisTest));
    }

    // Crée une demande de récupération pour l'adresse de ces tests.
    private static BackgroundEmailRequest CreateRecoveryRequestOfTheseTests()
    {
        return new BackgroundEmailRequest
        {
            TypeOfTheRequestedBackgroundEmail = BackgroundEmailRequestType.PasswordRecovery,
            EmailAddressOfTheRecoveryRequest = EmailAddressOfTheAccountInTheseTests
        };
    }

    [Fact]
    public async Task OneRequestSendsItsEmailWithoutWritingAnyError()
    {
        repositoryThatKeepsAccountsInMemory.SavedUsers.Add(new User { Email = EmailAddressOfTheAccountInTheseTests, IsEmailConfirmed = true });

        await CreateBackgroundTaskWithFakeTools().ProcessOneEmailRequestOfTheQueueAsync(CreateRecoveryRequestOfTheseTests());

        Assert.Single(senderThatKeepsTheEmailsInMemory.SentEmails);
        Assert.DoesNotContain(providerThatKeepsTheLogMessages.MessagesWrittenInTheLog, messageWrittenInTheTechnicalLog => messageWrittenInTheTechnicalLog.StartsWith("Error"));
    }

    // Sans cette trace, personne ne saurait que les e-mails ne partent plus. L'utilisateur est désigné par son identifiant (SS §7).
    [Fact]
    public async Task EmailFailureIsWrittenWithTheUserAndWithoutTheAddress()
    {
        var accountBeingTestedInThisRequest = new User { Email = EmailAddressOfTheAccountInTheseTests, IsEmailConfirmed = true };
        repositoryThatKeepsAccountsInMemory.SavedUsers.Add(accountBeingTestedInThisRequest);
        senderThatKeepsTheEmailsInMemory.EveryEmailFailsDuringThisTest = true;

        await CreateBackgroundTaskWithFakeTools().ProcessOneEmailRequestOfTheQueueAsync(CreateRecoveryRequestOfTheseTests());

        var messageWrittenInTheTechnicalLog = Assert.Single(providerThatKeepsTheLogMessages.MessagesWrittenInTheLog, messageReadInTheTechnicalLog => messageReadInTheTechnicalLog.StartsWith("Error"));
        Assert.Contains("n'a pas pu partir", messageWrittenInTheTechnicalLog);
        Assert.Contains("PasswordRecovery", messageWrittenInTheTechnicalLog);
        Assert.Contains(accountBeingTestedInThisRequest.Id.ToString(), messageWrittenInTheTechnicalLog);
        Assert.DoesNotContain(EmailAddressOfTheAccountInTheseTests, messageWrittenInTheTechnicalLog);
    }

    // Une panne ne doit pas arrêter la tâche : les demandes suivantes doivent encore partir.
    [Fact]
    public async Task DatabaseFailureIsWrittenAndDoesNotStopTheTask()
    {
        repositoryThatKeepsAccountsInMemory.SavedUsers.Add(new User { Email = EmailAddressOfTheAccountInTheseTests, IsEmailConfirmed = true });
        repositoryThatKeepsAccountsInMemory.DatabaseIsDownDuringThisTest = true;
        var backgroundTaskBeingTestedWithFakeTools = CreateBackgroundTaskWithFakeTools();

        await backgroundTaskBeingTestedWithFakeTools.ProcessOneEmailRequestOfTheQueueAsync(CreateRecoveryRequestOfTheseTests());

        Assert.Contains(providerThatKeepsTheLogMessages.MessagesWrittenInTheLog, messageWrittenInTheTechnicalLog =>
            messageWrittenInTheTechnicalLog.StartsWith("Error") && messageWrittenInTheTechnicalLog.Contains("a échoué"));
        Assert.Empty(senderThatKeepsTheEmailsInMemory.SentEmails);

        repositoryThatKeepsAccountsInMemory.DatabaseIsDownDuringThisTest = false;
        await backgroundTaskBeingTestedWithFakeTools.ProcessOneEmailRequestOfTheQueueAsync(CreateRecoveryRequestOfTheseTests());

        Assert.Single(senderThatKeepsTheEmailsInMemory.SentEmails);
    }
}
