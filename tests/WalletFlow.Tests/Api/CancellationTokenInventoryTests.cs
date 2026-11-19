using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Tests.Infrastructure;

namespace WalletFlow.Tests.Api;

// Une requête abandonnée par son client arrête son travail (SS §6) : chaque action asynchrone d'un contrôleur reçoit le jeton
// d'annulation de la requête, et chaque méthode publique asynchrone de l'Application, comme chaque méthode d'interface qui
// renvoie une tâche, en accepte un. L'analyseur CA2016, en erreur dans .editorconfig, oblige ensuite à le transmettre jusqu'à
// EF Core. Seules les écritures du journal de sécurité n'en prennent pas : un client qui raccroche ne doit pas pouvoir
// empêcher la trace de sa tentative.
public class CancellationTokenInventoryTests
{
    private static readonly HashSet<string> NonCancellableJournalWrites = new HashSet<string>
    {
        nameof(SecurityJournalService.SaveEventNowAsync),
        nameof(SecurityJournalService.SaveAdminActionNowAsync),
        nameof(ISecurityEventRepository.SaveEventAsync)
    };

    // Les méthodes publiques qui renvoient une tâche, déclarées par ce type lui-même.
    private static IEnumerable<MethodInfo> FindAsyncMethods(Type type)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => typeof(Task).IsAssignableFrom(method.ReturnType) && !method.IsSpecialName);
    }

    private static bool AcceptsToken(MethodInfo method)
    {
        return method.GetParameters().Any(parameter => parameter.ParameterType == typeof(CancellationToken));
    }

    [Fact]
    public void EveryAsyncControllerActionReceivesTheRequestToken()
    {
        var actions = typeof(Program).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(FindAsyncMethods)
            .ToList();

        Assert.True(actions.Count > 50);
        Assert.All(actions, action => Assert.True(AcceptsToken(action), action.DeclaringType!.Name + "." + action.Name + " ne reçoit pas le jeton."));
    }

    [Fact]
    public void EveryAsyncApplicationMethodAcceptsAToken()
    {
        var methods = typeof(DatabaseChangesSavingService).Assembly.GetTypes()
            .Where(type => type.IsPublic && !type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)))
            .SelectMany(FindAsyncMethods)
            .Where(method => !NonCancellableJournalWrites.Contains(method.Name))
            .ToList();

        Assert.True(methods.Count > 150);
        Assert.All(methods, method => Assert.True(AcceptsToken(method), method.DeclaringType!.Name + "." + method.Name + " n'accepte pas de jeton."));
    }

    // Le jeton reçu va bien jusqu'à la base : une lecture dont la requête est déjà abandonnée ne part jamais.
    [Fact]
    public async Task AbandonedRequestNeverReachesTheDatabase()
    {
        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new UserAccountRepository(databaseContext).FindByIdAsync(Guid.NewGuid(), new CancellationToken(true)));
    }
}
