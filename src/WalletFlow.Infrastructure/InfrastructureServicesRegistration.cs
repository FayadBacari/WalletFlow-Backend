using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WalletFlow.Infrastructure.Persistence;

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

        return servicesOfTheWalletFlowApplication;
    }
}
