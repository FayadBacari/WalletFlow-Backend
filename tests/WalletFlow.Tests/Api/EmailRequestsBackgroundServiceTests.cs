using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Notifications;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.Support;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.RecoveryRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie la tâche de fond qui envoie les e-mails de la file (SS §9).
public sealed class EmailRequestsBackgroundServiceTests : IDisposable
{
    private const string AccountEmail = "utilisateur@exemple.test";

    private static readonly IPAddress IpAddress = IPAddress.Parse("203.0.113.7");

    private readonly FakeUserAccountRepository accountRepository = new FakeUserAccountRepository();
    private readonly FakeSecurityEventRepository securityEventRepository = new FakeSecurityEventRepository();
    private readonly EmailRequestWakeUp wakeUp = new EmailRequestWakeUp();
    private readonly FakeEmailRequestRepository emailRequests;
    private readonly FakeAlertRepository alertRepository = new FakeAlertRepository();
    private readonly FakeSupportRequestRepository supportRepository = new FakeSupportRequestRepository();
    private readonly FakeDatabaseUnitOfWork unitOfWork;
    private readonly FakeEmailSender emailSender = new FakeEmailSender();
    private readonly FakeTimeProvider clock = new FakeTimeProvider();
    private readonly BackgroundTaskHealth tasksHealth = new BackgroundTaskHealth();
    private readonly MemoryLoggerProvider loggerProvider = new MemoryLoggerProvider();

    // Relie les faux accès à la fausse unité de travail, et la fausse file au réveil de la tâche.
    public EmailRequestsBackgroundServiceTests()
    {
        emailRequests = new FakeEmailRequestRepository(wakeUp);
        unitOfWork = new FakeDatabaseUnitOfWork(accountRepository, securityEventRepository, emailRequests, alertRepository, supportRepository);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // Crée la tâche à tester, branchée sur ces faux outils.
    private EmailRequestsBackgroundService CreateBackgroundTask()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUserAccountRepository>(accountRepository);
        services.AddSingleton<ISecurityEventRepository>(securityEventRepository);
        services.AddSingleton<IEmailRequestRepository>(emailRequests);
        services.AddSingleton<IAlertRepository>(alertRepository);
        services.AddSingleton<ISupportRequestRepository>(supportRepository);
        services.AddSingleton<IDatabaseUnitOfWork>(unitOfWork);
        services.AddSingleton<ISecureTokenGenerator>(new FakeSecureTokenGenerator());
        services.AddSingleton<IEmailSender>(emailSender);
        services.AddEmailTexts();
        services.AddSingleton(new PublicWebsiteSettings("https://walletflow.test"));
        services.AddSingleton<TimeProvider>(clock);
        services.AddScoped<SecurityJournalService>();
        services.AddScoped<LinkEmailPreparation>();
        services.AddScoped<NoticeEmailPreparation>();
        services.AddScoped<AlertAndSupportEmailPreparation>();
        services.AddScoped<EmailRequestProcessingService>();
        var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = new LoggerFactory(new ILoggerProvider[] { loggerProvider });

        return new EmailRequestsBackgroundService(
            wakeUp,
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            tasksHealth,
            new Logger<EmailRequestsBackgroundService>(loggerFactory));
    }

    // Ajoute à la fausse base un compte confirmé avec l'adresse de ces tests.
    private User AddAccount()
    {
        var account = new User { Email = AccountEmail, IsEmailConfirmed = true };
        accountRepository.SavedUsers.Add(account);

        return account;
    }

    // Les messages du journal technique de ce niveau.
    private List<string> ReadMessages(LogLevel level)
    {
        return loggerProvider.Messages.Where(message => message.StartsWith(level + " : ", StringComparison.Ordinal)).ToList();
    }

    [Fact]
    public async Task EveryDueRequestIsSentInOnePassWithoutWritingAnyError()
    {
        var account = AddAccount();
        emailRequests.SavedRequests.Add(EmailRequest.ForPasswordRecovery(AccountEmail, IpAddress, Now));
        emailRequests.SavedRequests.Add(EmailRequest.ForNewDevice(account.Id, IpAddress, Now));
        emailRequests.SavedRequests.Add(EmailRequest.ForPasswordChange(account.Id, IpAddress, Now));

        await CreateBackgroundTask().RunPassAsync();

        Assert.Equal(3, emailSender.SentEmails.Count);
        Assert.Empty(emailRequests.SavedRequests);
        Assert.Empty(ReadMessages(LogLevel.Warning));
        Assert.Empty(ReadMessages(LogLevel.Error));
    }

    // À l'arrêt de l'API, la demande reste due, sans essai raté compté : elle partira au redémarrage.
    [Fact]
    public async Task ShutdownLeavesTheRequestDueWithoutAFailedTry()
    {
        emailRequests.SavedRequests.Add(EmailRequest.ForPasswordRecovery(AccountEmail, IpAddress, Now));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateBackgroundTask().RunPassAsync(new CancellationToken(true)));

        Assert.Empty(emailSender.SentEmails);
        Assert.Equal(0, Assert.Single(emailRequests.SavedRequests).FailedTryCount);
        Assert.Empty(ReadMessages(LogLevel.Error));
    }

    [Fact]
    public async Task RequestWhoseTryIsLaterWaits()
    {
        var account = AddAccount();
        emailRequests.SavedRequests.Add(EmailRequest.ForNewDevice(account.Id, IpAddress, Now + TimeSpan.FromSeconds(1)));

        await CreateBackgroundTask().RunPassAsync();

        Assert.Empty(emailSender.SentEmails);
        Assert.Single(emailRequests.SavedRequests);
    }

    // Sans cette trace, personne ne saurait que les e-mails ne partent plus. L'utilisateur est désigné par son identifiant (SS §7).
    [Fact]
    public async Task FailedTryIsWrittenAsWarningWithTheUserAndWithoutTheAddress()
    {
        var account = AddAccount();
        emailSender.AlwaysFails = true;
        emailRequests.SavedRequests.Add(EmailRequest.ForPasswordRecovery(AccountEmail, IpAddress, Now));

        await CreateBackgroundTask().RunPassAsync();

        var message = Assert.Single(ReadMessages(LogLevel.Warning));
        Assert.Contains("n'a pas pu partir", message);
        Assert.Contains("PasswordRecovery", message);
        Assert.Contains(account.Id.ToString(), message);
        Assert.Contains("essai 1 raté", message);
        Assert.DoesNotContain(AccountEmail, message);
        Assert.Empty(ReadMessages(LogLevel.Error));
        Assert.Equal(1, Assert.Single(emailRequests.SavedRequests).FailedTryCount);
    }

    // Le dernier essai raté est une erreur : l'e-mail ne partira jamais.
    [Fact]
    public async Task LastFailedTryIsWrittenAsError()
    {
        var account = AddAccount();
        emailSender.AlwaysFails = true;
        emailRequests.SavedRequests.Add(new EmailRequest
        {
            Type = EmailRequestType.PasswordChange,
            UserId = account.Id,
            RequestedAt = Now,
            NextTryAt = Now,
            FailedTryCount = EmailRequestRules.MaximumTries - 1
        });

        await CreateBackgroundTask().RunPassAsync();

        var message = Assert.Single(ReadMessages(LogLevel.Error));
        Assert.Contains("abandonné", message);
        Assert.Contains("PasswordChange", message);
        Assert.Contains(account.Id.ToString(), message);
        Assert.DoesNotContain(AccountEmail, message);
        Assert.Empty(emailRequests.SavedRequests);
    }

    // Une panne imprévue ne doit pas arrêter la tâche, ni bloquer la file : la demande est réessayée plus tard,
    // et les suivantes partent tout de suite.
    [Fact]
    public async Task UnexpectedFailureIsWrittenAndTheRequestIsTriedLater()
    {
        var account = AddAccount();
        accountRepository.IsDown = true;
        var failingRequest = EmailRequest.ForPasswordRecovery(AccountEmail, IpAddress, Now - TimeSpan.FromSeconds(1));
        emailRequests.SavedRequests.Add(failingRequest);
        emailRequests.SavedRequests.Add(EmailRequest.ForEmailChangedNotice(account.Id, "ancienne@exemple.test", IpAddress, Now));
        var backgroundTask = CreateBackgroundTask();

        await backgroundTask.RunPassAsync();

        Assert.Contains(ReadMessages(LogLevel.Error), message => message.Contains("a échoué"));
        Assert.Equal("ancienne@exemple.test", Assert.Single(emailSender.SentEmails).RecipientAddress);
        Assert.Equal(1, failingRequest.FailedTryCount);
        Assert.Equal(Now + TimeSpan.FromMinutes(1), failingRequest.NextTryAt);

        accountRepository.IsDown = false;
        clock.Advance(TimeSpan.FromMinutes(1));
        await backgroundTask.RunPassAsync();

        Assert.Equal(2, emailSender.SentEmails.Count);
        Assert.Empty(emailRequests.SavedRequests);
    }

    // La file ne peut pas être lue : la panne va au journal technique, et la tâche réessaie au passage suivant.
    [Fact]
    public async Task UnreadableQueueIsWrittenAndRetriedOnTheNextPass()
    {
        AddAccount();
        emailRequests.SavedRequests.Add(EmailRequest.ForPasswordRecovery(AccountEmail, IpAddress, Now));
        emailRequests.IsDown = true;
        var backgroundTask = CreateBackgroundTask();

        await backgroundTask.RunPassAsync();

        Assert.Contains(ReadMessages(LogLevel.Error), message => message.Contains("ne peut pas être lue"));
        Assert.Empty(emailSender.SentEmails);

        emailRequests.IsDown = false;
        await backgroundTask.RunPassAsync();

        Assert.Single(emailSender.SentEmails);
    }

    // Un serveur d'e-mails en panne fait échouer chaque passage qui essaie d'envoyer. Les passages à vide entre deux essais ne
    // cachent rien : au troisième essai raté, la tâche est en panne sur son point de santé.
    [Fact]
    public async Task MailServerDownMakesTheTaskFailingDespiteIdlePasses()
    {
        AddAccount();
        emailSender.AlwaysFails = true;
        var backgroundTask = CreateBackgroundTask();
        await backgroundTask.StartAsync(CancellationToken.None);

        try
        {
            emailRequests.PrepareRequest(EmailRequest.ForPasswordRecovery(AccountEmail, IpAddress, Now));
            Assert.True(await unitOfWork.TrySaveChangesAsync());
            await WaitUntilAsync(() => emailSender.FailedSendCount == 1);

            for (var attempt = 2; attempt <= BackgroundTaskHealth.FailedPassesBeforeAlert; attempt++)
            {
                wakeUp.Signal();
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                clock.Advance(TimeSpan.FromDays(1));
                wakeUp.Signal();
                await WaitUntilAsync(() => emailSender.FailedSendCount == attempt);
            }

            await WaitUntilAsync(() => tasksHealth.FindFailingTasks(clock.GetUtcNow()).Count == 1);
        }
        finally
        {
            await backgroundTask.StopAsync(CancellationToken.None);
        }

        Assert.EndsWith("3 passages de suite en échec.", Assert.Single(tasksHealth.FindFailingTasks(clock.GetUtcNow())));
    }

    // La tâche n'attend pas son passage de secours : une demande enregistrée la réveille, et l'e-mail part aussitôt.
    [Fact]
    public async Task NewRequestWakesTheTaskUpRightAway()
    {
        AddAccount();
        var backgroundTask = CreateBackgroundTask();
        await backgroundTask.StartAsync(CancellationToken.None);

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            emailRequests.PrepareRequest(EmailRequest.ForPasswordRecovery(AccountEmail, IpAddress, Now));
            Assert.True(await unitOfWork.TrySaveChangesAsync());

            await WaitUntilAsync(() => emailSender.CountSentEmailsTo(AccountEmail) == 1);
        }
        finally
        {
            await backgroundTask.StopAsync(CancellationToken.None);
        }

        Assert.True(EmailRequestsBackgroundService.MaximumWait >= TimeSpan.FromSeconds(30));
    }

    // Libère le journal technique et le réveil de la file à la fin du test.
    public void Dispose()
    {
        loggerProvider.Dispose();
        wakeUp.Dispose();
    }
}
