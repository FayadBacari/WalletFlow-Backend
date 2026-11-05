using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Security;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Infrastructure.Emails;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Infrastructure.Security;

namespace WalletFlow.Infrastructure;

// Enregistre les services de la couche Infrastructure dans l'injection de dépendances.
public static class InfrastructureServicesRegistration
{
    // Branche EF Core sur la base PostgreSQL désignée par la chaîne de connexion.
    public static IServiceCollection AddWalletFlowDatabaseAccessToTheServices(
        this IServiceCollection servicesOfTheWalletFlowApplication,
        string connectionStringOfTheWalletFlowDatabase)
    {
        servicesOfTheWalletFlowApplication.AddDbContext<WalletFlowDatabaseContext>(optionsOfTheDatabaseContextToConfigure =>
        {
            optionsOfTheDatabaseContextToConfigure.UseNpgsql(connectionStringOfTheWalletFlowDatabase);
        });

        servicesOfTheWalletFlowApplication.AddScoped<IUserAccountRepository, UserAccountRepository>();
        servicesOfTheWalletFlowApplication.AddScoped<IUserSessionRepository, UserSessionRepository>();
        servicesOfTheWalletFlowApplication.AddScoped<ISecurityEventRepository, SecurityEventRepository>();
        servicesOfTheWalletFlowApplication.AddScoped<ITwoFactorAuthenticationRepository, TwoFactorAuthenticationRepository>();

        return servicesOfTheWalletFlowApplication;
    }

    // Ajoute le hachage des mots de passe, la création des jetons, les limites par adresse e-mail et les outils de la double authentification.
    public static IServiceCollection AddWalletFlowSecurityToolsToTheServices(this IServiceCollection servicesOfTheWalletFlowApplication)
    {
        servicesOfTheWalletFlowApplication.AddSingleton<IPasswordHasher, LibsodiumPasswordHasher>();
        servicesOfTheWalletFlowApplication.AddSingleton<ISecureTokenGenerator, RandomSecureTokenGenerator>();
        servicesOfTheWalletFlowApplication.AddSingleton<IEmailAddressRequestLimiter, InMemoryEmailAddressRequestLimiter>();
        servicesOfTheWalletFlowApplication.AddSingleton<IPasswordRecoveryRequestLimiter, InMemoryPasswordRecoveryRequestLimiter>();
        servicesOfTheWalletFlowApplication.AddSingleton<IAuthenticatorCodeTool, OtpNetAuthenticatorCodeTool>();
        servicesOfTheWalletFlowApplication.AddSingleton<ILoginsWaitingForTheirCodeStore, InMemoryLoginsWaitingForTheirCodeStore>();
        servicesOfTheWalletFlowApplication.AddSingleton<ISecretsWaitingForActivationStore, InMemorySecretsWaitingForActivationStore>();
        servicesOfTheWalletFlowApplication.AddSingleton<IUsedAuthenticatorCodesRegister, InMemoryUsedAuthenticatorCodesRegister>();

        return servicesOfTheWalletFlowApplication;
    }

    // Ajoute le chiffrement des secrets de double authentification, avec cette clé écrite en Base64.
    public static IServiceCollection AddTotpSecretEncryptionToTheServices(
        this IServiceCollection servicesOfTheWalletFlowApplication,
        string encryptionKeyWrittenInBase64)
    {
        servicesOfTheWalletFlowApplication.AddSingleton<ITotpSecretProtector>(new AesGcmTotpSecretProtector(encryptionKeyWrittenInBase64));

        return servicesOfTheWalletFlowApplication;
    }

    // Ajoute la file des e-mails préparés en arrière-plan.
    public static IServiceCollection AddBackgroundEmailRequestQueueToTheServices(this IServiceCollection servicesOfTheWalletFlowApplication)
    {
        servicesOfTheWalletFlowApplication.AddSingleton<IBackgroundEmailRequestQueue, InMemoryBackgroundEmailRequestQueue>();

        return servicesOfTheWalletFlowApplication;
    }

    // Écrit les e-mails dans un dossier local, au lieu de les envoyer (développement seulement).
    public static IServiceCollection AddEmailsWrittenToLocalFolderToTheServices(
        this IServiceCollection servicesOfTheWalletFlowApplication,
        string pathOfTheFolderThatReceivesTheEmails)
    {
        servicesOfTheWalletFlowApplication.AddSingleton<IEmailSender>(new LocalFolderEmailSender(pathOfTheFolderThatReceivesTheEmails));

        return servicesOfTheWalletFlowApplication;
    }
}
