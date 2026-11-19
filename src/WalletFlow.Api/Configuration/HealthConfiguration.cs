using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Application.Persistence;

namespace WalletFlow.Api.Configuration;

// Les points de santé de l'API, pour l'hébergeur qui la surveille et la redémarre si besoin (SS §11). « live » dit seulement que
// l'API tourne ; « ready » dit aussi que la base répond. « tasks » dit si les tâches de fond vont bien : un service de surveillance
// qui l'interroge alerte quand l'une d'elles est en panne. Il ne compte pas pour « ready » : une tâche en panne ne doit pas retirer
// l'API du trafic. Tous s'ouvrent sans session et répondent juste « Healthy » ou « Unhealthy » (503), sans aucun détail technique
// (SS §10) : la tâche en panne se lit dans le journal technique.
public static class HealthConfiguration
{
    public const string LivePath = "/health/live";
    public const string ReadyPath = "/health/ready";
    public const string TasksPath = "/health/tasks";

    // Au-delà de ce temps, la base est comptée comme injoignable : l'hébergeur ne doit pas attendre la réponse sans fin.
    public static readonly TimeSpan DatabaseTimeout = TimeSpan.FromSeconds(5);

    private const string ReadinessTag = "pret";
    private const string TasksTag = "taches";

    // Ajoute la vérification de la base, qui ne compte que pour « ready », et celle des tâches de fond, qui ne compte que pour « tasks ».
    public static IServiceCollection AddWalletFlowHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("base", tags: new[] { ReadinessTag }, timeout: DatabaseTimeout)
            .AddCheck<BackgroundTasksHealthCheck>("taches", tags: new[] { TasksTag });

        return services;
    }

    // Relie les trois points de santé, ouverts sans session.
    public static void MapWalletFlowHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(LivePath, new HealthCheckOptions { Predicate = check => false }).AllowAnonymous();
        endpoints.MapHealthChecks(ReadyPath, new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadinessTag) }).AllowAnonymous();
        endpoints.MapHealthChecks(TasksPath, new HealthCheckOptions { Predicate = check => check.Tags.Contains(TasksTag) }).AllowAnonymous();
    }

    // Vérifie que la base répond.
    private sealed class DatabaseHealthCheck : IHealthCheck
    {
        private readonly IDatabaseHealth databaseHealth;

        // Reçoit de quoi interroger la base.
        public DatabaseHealthCheck(IDatabaseHealth databaseHealth)
        {
            this.databaseHealth = databaseHealth;
        }

        // Répond « sain » si la base répond, « malade » sinon (nom imposé par le framework).
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            return await databaseHealth.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("La base ne répond pas.");
        }
    }

    // Vérifie que les tâches de fond vont bien : aucune n'échoue plusieurs passages de suite, et toutes réussissent à temps.
    private sealed class BackgroundTasksHealthCheck : IHealthCheck
    {
        private readonly BackgroundTaskHealth tasksHealth;
        private readonly TimeProvider clock;

        // Reçoit le bilan des tâches et l'horloge.
        public BackgroundTasksHealthCheck(BackgroundTaskHealth tasksHealth, TimeProvider clock)
        {
            this.tasksHealth = tasksHealth;
            this.clock = clock;
        }

        // Répond « sain » si toutes les tâches vont bien, « malade » avec la liste des tâches en panne sinon (nom imposé par le
        // framework). La liste va au journal technique, jamais dans la réponse.
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var failingTasks = tasksHealth.FindFailingTasks(clock.GetUtcNow());

            return Task.FromResult(failingTasks.Count == 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy(string.Join(" ", failingTasks)));
        }
    }
}
