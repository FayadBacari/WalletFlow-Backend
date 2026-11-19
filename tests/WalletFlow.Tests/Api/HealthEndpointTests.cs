using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Api.Configuration;

namespace WalletFlow.Tests.Api;

// Vérifie les points de santé de l'API (SS §10 et §11) : ouverts sans session, ils disent si l'API tourne et si la base répond,
// sans aucun détail technique.
public class HealthEndpointTests
{
    // « live » répond « Healthy » sans session, même quand la base est en panne : l'API tourne, il ne faut pas la redémarrer.
    [Fact]
    public async Task LiveAnswersHealthyEvenWhenTheDatabaseIsDown()
    {
        using var api = new ApiFactoryInProduction();
        api.DatabaseHealth.IsDown = true;

        var response = await api.CreateClient().GetAsync(HealthConfiguration.LivePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        SecurityHeadersAssertions.AssertExactSecurityHeaders(response);
    }

    // « ready » répond « Healthy » quand la base répond, et « Unhealthy » avec 503 quand elle ne répond pas, sans dire pourquoi.
    [Fact]
    public async Task ReadyFollowsTheDatabase()
    {
        using var api = new ApiFactoryInProduction();
        var client = api.CreateClient();

        var healthy = await client.GetAsync(HealthConfiguration.ReadyPath);
        api.DatabaseHealth.IsDown = true;
        var unhealthy = await client.GetAsync(HealthConfiguration.ReadyPath);

        Assert.Equal((HttpStatusCode.OK, "Healthy"), (healthy.StatusCode, await healthy.Content.ReadAsStringAsync()));
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "Unhealthy"), (unhealthy.StatusCode, await unhealthy.Content.ReadAsStringAsync()));
        SecurityHeadersAssertions.AssertExactSecurityHeaders(unhealthy);
    }

    // Les points de santé restent ouverts sans session même si une règle par défaut exige un jour une session pour tout ce qui
    // n'est pas un contrôleur : l'hébergeur qui sonde l'API n'a pas de compte.
    [Fact]
    public async Task HealthEndpointsStayOpenUnderADefaultSessionRule()
    {
        using var api = new ApiFactoryInProduction();
        using var apiWithDefaultRule = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<AuthorizationOptions>(options =>
                options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())));
        var client = apiWithDefaultRule.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(HealthConfiguration.LivePath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(HealthConfiguration.ReadyPath)).StatusCode);
    }

    // Hors du développement, les points de santé ne répondent qu'aux noms du site, comme toutes les routes (SS §6).
    [Fact]
    public async Task HealthEndpointsOnlyAnswerTheSiteNames()
    {
        using var api = new ApiFactoryInProduction();
        var client = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://site-piege.example") });

        var response = await client.GetAsync(HealthConfiguration.LivePath);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // La base compte comme injoignable au bout de 5 secondes : l'hébergeur n'attend pas la réponse sans fin.
    [Fact]
    public void DatabaseCheckHasATimeout()
    {
        using var api = new ApiFactoryWithFakes();

        var registration = api.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.Single(check => check.Name == "base");

        Assert.Equal(TimeSpan.FromSeconds(5), registration.Timeout);
    }

    // En dehors des contrôleurs, dont les routes publiques sont comptées ailleurs, l'API de production n'ouvre que les trois points
    // de santé : une route ajoutée sans session ne passe pas inaperçue.
    [Fact]
    public void OnlyTheHealthEndpointsLiveOutsideTheControllers()
    {
        using var api = new ApiFactoryInProduction();
        api.CreateClient();

        var routes = api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() == null)
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new List<string?> { HealthConfiguration.LivePath, HealthConfiguration.ReadyPath, HealthConfiguration.TasksPath }, routes);
    }

    // « tasks » répond « Healthy » quand les tâches de fond vont bien.
    [Fact]
    public async Task TasksAnswersHealthyWhenEveryTaskIsWell()
    {
        using var api = new ApiFactoryInProduction();

        var response = await api.CreateClient().GetAsync(HealthConfiguration.TasksPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    // Une tâche en panne rend « tasks » malade, sans dire laquelle dans la réponse, et laisse « ready » sain : l'API reste dans le
    // trafic.
    [Fact]
    public async Task FailingTaskMakesTasksUnhealthyButNotReady()
    {
        using var api = new ApiFactoryInProduction();
        var client = api.CreateClient();
        var tasksHealth = api.Services.GetRequiredService<BackgroundTaskHealth>();
        tasksHealth.Watch("TacheDeTest", TimeSpan.FromHours(1), api.Clock.GetUtcNow());
        for (var failure = 1; failure <= BackgroundTaskHealth.FailedPassesBeforeAlert; failure++)
        {
            tasksHealth.RecordPass("TacheDeTest", BackgroundTaskHealth.PassOutcome.Failed, api.Clock.GetUtcNow());
        }

        var tasks = await client.GetAsync(HealthConfiguration.TasksPath);
        var ready = await client.GetAsync(HealthConfiguration.ReadyPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, tasks.StatusCode);
        Assert.Equal("Unhealthy", await tasks.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        SecurityHeadersAssertions.AssertExactSecurityHeaders(tasks);
    }
}
