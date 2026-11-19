using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Api;

// Vérifie la tâche quotidienne qui rechiffre les secrets de double authentification avec la clé actuelle (SS §7).
public sealed class TotpSecretReEncryptionBackgroundServiceTests : IDisposable
{
    private readonly FakeUserAccountRepository accountRepository = new FakeUserAccountRepository();
    private readonly MemoryLoggerProvider loggerProvider = new MemoryLoggerProvider();
    private readonly string oldKey = TwoFactorTestTools.CreateEncryptionKey();
    private readonly AesGcmTotpSecretProtector keyRing;

    // La clé 2 est la clé actuelle ; la clé 1 ne sert plus qu'à déchiffrer.
    public TotpSecretReEncryptionBackgroundServiceTests()
    {
        keyRing = new AesGcmTotpSecretProtector(2, TwoFactorTestTools.CreateEncryptionKey(), new Dictionary<int, string> { [1] = oldKey });
    }

    // Crée la tâche à tester, branchée sur ces faux outils.
    private TotpSecretReEncryptionBackgroundService CreateTask()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUserAccountRepository>(accountRepository);
        services.AddSingleton<IDatabaseUnitOfWork>(new FakeDatabaseUnitOfWork(accountRepository));
        services.AddSingleton<ITotpSecretProtector>(keyRing);
        services.AddScoped<DatabaseChangesSavingService>();
        services.AddScoped<TotpSecretReEncryptionService>();
        var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = new LoggerFactory(new ILoggerProvider[] { loggerProvider });

        return new TotpSecretReEncryptionBackgroundService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new FakeTimeProvider(),
            new BackgroundTaskHealth(),
            new Logger<TotpSecretReEncryptionBackgroundService>(loggerFactory));
    }

    // Ajoute un compte dont le secret est chiffré avec cette clé.
    private User AddAccount(AesGcmTotpSecretProtector protector)
    {
        var account = new User { Email = "compte-" + Guid.NewGuid().ToString("N") + "@exemple.test", IsEmailConfirmed = true };
        account.ActivateTwoFactor(protector.Encrypt("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", account.Id), 1000);
        accountRepository.SavedUsers.Add(account);

        return account;
    }

    // Un passage rechiffre tous les secrets de l'ancienne clé, sur plusieurs paquets, et le dit au journal technique.
    [Fact]
    public async Task OnePassReEncryptsEverySecretOfTheOldKey()
    {
        var oldProtector = new AesGcmTotpSecretProtector(oldKey);
        var accounts = Enumerable.Range(0, TotpSecretReEncryptionService.AccountBatchSize + 30).Select(_ => AddAccount(oldProtector)).ToList();

        await CreateTask().RunPassAsync();

        Assert.All(accounts, account => Assert.True(keyRing.UsesCurrentKey(account.EncryptedTotpSecret!)));
        Assert.Contains(loggerProvider.Messages, message => message.Contains("130 secret(s)") && message.Contains("0 échec(s)"));
    }

    // Un secret qu'aucune clé configurée ne déchiffre va au journal technique, désigné par son compte, sans arrêter les autres.
    [Fact]
    public async Task UnreadableSecretIsWrittenInTheLogWithoutStoppingThePass()
    {
        var lostKeyAccount = AddAccount(new AesGcmTotpSecretProtector(3, TwoFactorTestTools.CreateEncryptionKey(), new Dictionary<int, string>()));
        var oldKeyAccount = AddAccount(new AesGcmTotpSecretProtector(oldKey));

        await CreateTask().RunPassAsync();

        Assert.True(keyRing.UsesCurrentKey(oldKeyAccount.EncryptedTotpSecret!));
        Assert.StartsWith("k3$", lostKeyAccount.EncryptedTotpSecret);
        Assert.Contains(loggerProvider.Messages, message => message.StartsWith("Error", StringComparison.Ordinal) && message.Contains(lostKeyAccount.Id.ToString()));
        Assert.Contains(loggerProvider.Messages, message => message.Contains("1 secret(s)") && message.Contains("1 échec(s)"));
    }

    // La tâche tourne une fois par jour : le test ne peut pas attendre un jour pour le vérifier.
    [Fact]
    public void PassRunsOncePerDay()
    {
        Assert.Equal(TimeSpan.FromDays(1), TotpSecretReEncryptionBackgroundService.PassInterval);
    }

    // Libère le journal technique à la fin du test.
    public void Dispose()
    {
        loggerProvider.Dispose();
    }
}
