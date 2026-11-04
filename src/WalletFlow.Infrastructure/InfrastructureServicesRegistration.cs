using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
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

        return servicesOfTheWalletFlowApplication;
    }

    // Ajoute le hachage des mots de passe, la création des jetons et la limite par adresse e-mail.
    public static IServiceCollection AddWalletFlowSecurityToolsToTheServices(this IServiceCollection servicesOfTheWalletFlowApplication)
    {
        servicesOfTheWalletFlowApplication.AddSingleton<IPasswordHasher, LibsodiumPasswordHasher>();
        servicesOfTheWalletFlowApplication.AddSingleton<ISecureTokenGenerator, RandomSecureTokenGenerator>();
        servicesOfTheWalletFlowApplication.AddSingleton<IEmailAddressRequestLimiter, InMemoryEmailAddressRequestLimiter>();

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
