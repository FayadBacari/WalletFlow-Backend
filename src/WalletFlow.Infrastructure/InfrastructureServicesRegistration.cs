using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.AccountSettings;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Categories;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Infrastructure.BankData;
using WalletFlow.Infrastructure.Emails;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Infrastructure.Security;

namespace WalletFlow.Infrastructure;

// Enregistre les services de la couche Infrastructure dans l'injection de dépendances.
public static class InfrastructureServicesRegistration
{
    // Branche EF Core sur la base PostgreSQL désignée par la chaîne de connexion.
    public static IServiceCollection AddWalletFlowDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<WalletFlowDatabaseContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<IDatabaseUnitOfWork, EntityFrameworkDatabaseUnitOfWork>();
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();
        services.AddScoped<IUserSessionRepository, UserSessionRepository>();
        services.AddScoped<ISecurityEventRepository, SecurityEventRepository>();
        services.AddScoped<ITwoFactorAuthenticationRepository, TwoFactorAuthenticationRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IBankConnectionRepository, BankConnectionRepository>();

        return services;
    }

    // Ajoute le hachage des mots de passe et des codes de secours, la création des jetons, les limites par adresse e-mail
    // et les outils de la double authentification.
    public static IServiceCollection AddWalletFlowSecurityTools(this IServiceCollection services)
    {
        // Un seul limiteur pour tous les calculs Argon2id : autant de calculs à la fois que de cœurs, pas plus.
        services.AddSingleton(new ArgonComputationsLimiter(Environment.ProcessorCount));
        services.AddSingleton<IPasswordHasher, LibsodiumPasswordHasher>();
        services.AddSingleton<IBackupCodeHasher, LibsodiumBackupCodeHasher>();
        services.AddSingleton<ISecureTokenGenerator, RandomSecureTokenGenerator>();
        services.AddSingleton<IEmailAddressRequestLimiter, InMemoryEmailAddressRequestLimiter>();
        services.AddSingleton<IPasswordRecoveryRequestLimiter, InMemoryPasswordRecoveryRequestLimiter>();
        services.AddSingleton<IEmailChangeRequestLimiter, InMemoryEmailChangeRequestLimiter>();
        services.AddSingleton<IAuthenticatorCodeTool, OtpNetAuthenticatorCodeTool>();
        services.AddSingleton<IWaitingLoginStore, InMemoryWaitingLoginStore>();
        services.AddSingleton<IWaitingSecretStore, InMemoryWaitingSecretStore>();
        services.AddSingleton<IUsedAuthenticatorCodesRegister, InMemoryUsedAuthenticatorCodesRegister>();

        return services;
    }

    // Ajoute le chiffrement des secrets de double authentification, avec cette clé écrite en Base64.
    public static IServiceCollection AddTotpSecretEncryption(
        this IServiceCollection services,
        string base64Key)
    {
        services.AddSingleton<ITotpSecretProtector>(new AesGcmTotpSecretProtector(base64Key));

        return services;
    }

    // Ajoute la file des e-mails préparés en arrière-plan.
    public static IServiceCollection AddEmailQueue(this IServiceCollection services)
    {
        services.AddSingleton<IBackgroundEmailRequestQueue, InMemoryBackgroundEmailRequestQueue>();

        return services;
    }

    // Ajoute le texte en français des e-mails envoyés aux utilisateurs, quel que soit le moyen de les envoyer.
    public static IServiceCollection AddEmailTexts(this IServiceCollection services)
    {
        services.AddSingleton<IAccountRegistrationEmails, FrenchAccountRegistrationEmails>();
        services.AddSingleton<IAccountRecoveryEmails, FrenchAccountRecoveryEmails>();
        services.AddSingleton<IAccountChangeEmails, FrenchAccountChangeEmails>();
        return services;
    }

    // Écrit les e-mails dans un dossier local, au lieu de les envoyer (développement seulement).
    public static IServiceCollection AddLocalFolderEmails(
        this IServiceCollection services,
        string folderPath)
    {
        services.AddSingleton<IEmailSender>(new LocalFolderEmailSender(folderPath));

        return services;
    }

    // Branche le prestataire bancaire Enable Banking, avec l'identifiant de l'application WalletFlow et sa clé privée (SS §7).
    // La clé est lue tout de suite : une clé illisible fait refuser le démarrage de l'API, au lieu d'échouer plus tard.
    public static IServiceCollection AddEnableBanking(
        this IServiceCollection services,
        string applicationId,
        string privateKeyPem)
    {
        var tokenSigner = new EnableBankingTokenSigner(applicationId, privateKeyPem, TimeProvider.System);
        var httpClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            BaseAddress = EnableBankingClient.ApiAddress,
            Timeout = TimeSpan.FromSeconds(30)
        };

        services.AddSingleton(tokenSigner);
        services.AddSingleton<IBankDataProvider>(serviceProvider => new EnableBankingClient(
            httpClient,
            tokenSigner,
            serviceProvider.GetRequiredService<TimeProvider>(),
            serviceProvider.GetRequiredService<ILogger<EnableBankingClient>>()));

        return services;
    }

    // En développement, tant qu'Enable Banking n'est pas configuré : les routes des banques répondent 503.
    public static IServiceCollection AddBankProviderNotConfigured(this IServiceCollection services)
    {
        services.AddSingleton<IBankDataProvider, BankProviderNotConfigured>();

        return services;
    }

    // Ajoute la mémoire des autorisations bancaires commencées, la limite des mises à jour par utilisateur,
    // et l'horaire des mises à jour en arrière-plan.
    public static IServiceCollection AddBankConnectionTools(this IServiceCollection services)
    {
        services.AddSingleton<IBankAuthorizationStore, InMemoryBankAuthorizationStore>();
        services.AddSingleton<IBankUpdateLimiter, InMemoryBankUpdateLimiter>();
        services.AddSingleton<IBankUpdateSchedule, InMemoryBankUpdateSchedule>();

        return services;
    }
}
