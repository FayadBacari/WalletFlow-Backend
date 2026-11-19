using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.AccountSettings;
using WalletFlow.Application.Administration;
using WalletFlow.Application.Bags;
using WalletFlow.Application.Suggestions;
using WalletFlow.Application.Support;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Categories;
using WalletFlow.Application.DataExport;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Notifications;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.Transactions;
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
        services.AddScoped<IDatabaseHealth, DatabaseHealth>();
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();
        services.AddScoped<IUserSessionRepository, UserSessionRepository>();
        services.AddScoped<ISecurityEventRepository, SecurityEventRepository>();
        services.AddScoped<ITwoFactorAuthenticationRepository, TwoFactorAuthenticationRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IBankConnectionRepository, BankConnectionRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IBagRepository, BagRepository>();
        services.AddScoped<ISuggestionRepository, SuggestionRepository>();
        services.AddScoped<IResourceOwnershipRepository, ResourceOwnershipRepository>();
        services.AddScoped<IEmailRequestRepository, EmailRequestRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<ISupportRequestRepository, SupportRequestRepository>();
        services.AddScoped<IAdministrationRepository, AdministrationRepository>();
        services.AddScoped<IDataExportRepository, DataExportRepository>();

        return services;
    }

    // Ajoute le hachage des mots de passe et des codes de secours, la création des jetons, les limites par adresse e-mail,
    // les outils de la double authentification et la recherche des mots de passe des fuites connues.
    public static IServiceCollection AddWalletFlowSecurityTools(this IServiceCollection services)
    {
        // Un seul limiteur pour tous les calculs Argon2id : autant de calculs à la fois que de cœurs, pas plus. Le conteneur le crée
        // lui-même : il ne libère à l'arrêt que ce qu'il a créé.
        services.AddSingleton(serviceProvider => new ArgonComputationsLimiter(Environment.ProcessorCount));
        services.AddSingleton<IPasswordHasher, LibsodiumPasswordHasher>();
        services.AddSingleton<IBackupCodeHasher, LibsodiumBackupCodeHasher>();
        services.AddSingleton<ISecureTokenGenerator, RandomSecureTokenGenerator>();
        services.AddSingleton<IEmailAddressRequestLimiter, InMemoryEmailAddressRequestLimiter>();
        services.AddSingleton<IPasswordRecoveryRequestLimiter, InMemoryPasswordRecoveryRequestLimiter>();
        services.AddSingleton<IEmailChangeRequestLimiter, InMemoryEmailChangeRequestLimiter>();
        services.AddSingleton<IDataExportLimiter, InMemoryDataExportLimiter>();
        services.AddSingleton<IAuthenticatorCodeTool, OtpNetAuthenticatorCodeTool>();
        services.AddSingleton<IWaitingLoginStore, InMemoryWaitingLoginStore>();
        services.AddSingleton<IWaitingSecretStore, InMemoryWaitingSecretStore>();

        // Les mots de passe des fuites connues sont refusés (SS §2). Un seul client HTTP pour toute l'API, renouvelé toutes les
        // 5 minutes pour suivre les changements d'adresse du service.
        var pwnedPasswordsClient = PwnedPasswordsChecker.CreateHttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        services.AddSingleton<IBreachedPasswordChecker>(serviceProvider => new PwnedPasswordsChecker(
            pwnedPasswordsClient,
            serviceProvider.GetRequiredService<ILogger<PwnedPasswordsChecker>>()));

        return services;
    }

    // Ajoute le chiffrement des secrets de double authentification : la clé actuelle et ses anciennes clés, écrites en Base64.
    public static IServiceCollection AddTotpSecretEncryption(
        this IServiceCollection services,
        int currentKeyId,
        string currentBase64Key,
        IReadOnlyDictionary<int, string> previousBase64Keys)
    {
        services.AddSingleton<ITotpSecretProtector>(new AesGcmTotpSecretProtector(currentKeyId, currentBase64Key, previousBase64Keys));

        return services;
    }

    // Ajoute le réveil de la tâche de fond des e-mails : la file elle-même est gardée en base (voir AddWalletFlowDatabase).
    public static IServiceCollection AddEmailQueue(this IServiceCollection services)
    {
        services.AddSingleton<EmailRequestWakeUp>();

        return services;
    }

    // Ajoute le texte en français des e-mails envoyés aux utilisateurs, quel que soit le moyen de les envoyer.
    public static IServiceCollection AddEmailTexts(this IServiceCollection services)
    {
        services.AddSingleton<IAccountRegistrationEmails, FrenchAccountRegistrationEmails>();
        services.AddSingleton<IAccountRecoveryEmails, FrenchAccountRecoveryEmails>();
        services.AddSingleton<IAccountChangeEmails, FrenchAccountChangeEmails>();
        services.AddSingleton<ISecurityAlertEmails, FrenchSecurityAlertEmails>();
        services.AddSingleton<ISupportEmails, FrenchSupportEmails>();
        services.AddSingleton<IAlertEmails, FrenchAlertEmails>();
        return services;
    }

    // Écrit les e-mails dans un dossier local, au lieu de les envoyer (développement seulement).
    public static IServiceCollection AddLocalFolderEmails(
        this IServiceCollection services,
        string folderPath)
    {
        services.AddSingleton<IEmailSender>(serviceProvider =>
            new LocalFolderEmailSender(folderPath, serviceProvider.GetRequiredService<TimeProvider>()));

        return services;
    }

    // Hors du développement : les e-mails partent par le serveur SMTP du prestataire (SS §9).
    public static IServiceCollection AddSmtpEmails(this IServiceCollection services, SmtpServerSettings server)
    {
        services.AddSingleton<IEmailSender>(serviceProvider =>
            new SmtpEmailSender(server, serviceProvider.GetRequiredService<ILogger<SmtpEmailSender>>()));

        return services;
    }

    // Hors du développement, sans réglages SMTP : l'API refuse de démarrer avec lui (voir Program.cs).
    public static IServiceCollection AddEmailSenderNotConfigured(this IServiceCollection services)
    {
        services.AddSingleton<IEmailSender, EmailSenderNotConfigured>();

        return services;
    }

    // Branche le prestataire bancaire Enable Banking, avec l'identifiant de l'application WalletFlow et sa clé privée (SS §7).
    // La clé est lue tout de suite, une première fois : une clé illisible fait refuser le démarrage de l'API, au lieu d'échouer
    // plus tard. Le signataire gardé, lui, date ses jetons avec l'horloge de l'application, comme tout le reste.
    public static IServiceCollection AddEnableBanking(
        this IServiceCollection services,
        string applicationId,
        string privateKeyPem)
    {
        using (new EnableBankingTokenSigner(applicationId, privateKeyPem, TimeProvider.System))
        {
        }

        var httpClient = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            BaseAddress = EnableBankingClient.ApiAddress,
            Timeout = TimeSpan.FromSeconds(30)
        };

        services.AddSingleton(serviceProvider => new EnableBankingTokenSigner(
            applicationId,
            privateKeyPem,
            serviceProvider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IBankDataProvider>(serviceProvider => new EnableBankingClient(
            httpClient,
            serviceProvider.GetRequiredService<EnableBankingTokenSigner>(),
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
