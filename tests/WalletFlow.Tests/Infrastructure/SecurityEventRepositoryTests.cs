using System.Net;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le journal de sécurité dans la vraie base PostgreSQL locale (SS §11).
// Chaque test travaille dans une transaction annulée à la fin : la base reste vide.
public class SecurityEventRepositoryTests
{
    private static readonly DateTime EventDate = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    // SS §11 : la fonction de la base efface les événements de plus d'un an, et garde les autres. La base compte avec sa propre
    // horloge : une heure de marge de chaque côté rend le test sûr, quelle que soit l'heure de la machine.
    [Fact]
    public async Task OnlyEventsOlderThanOneYearAreDeleted()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new SecurityEventRepository(databaseContext);
        var oneYearAgo = DateTime.UtcNow.AddYears(-1);
        var oldEvent = new SecurityEvent { Type = SecurityEventType.Login, Result = SecurityEventResult.Failure, OccurredAt = oneYearAgo.AddHours(-1) };
        var recentEvent = new SecurityEvent { Type = SecurityEventType.Login, Result = SecurityEventResult.Failure, OccurredAt = oneYearAgo.AddHours(1) };
        await repository.SaveEventAsync(oldEvent);
        await repository.SaveEventAsync(recentEvent);

        var deletedCount = await repository.DeleteExpiredEventsAsync();

        Assert.True(deletedCount >= 1);
        Assert.False(await databaseContext.SecurityEvents.AnyAsync(row => row.Id == oldEvent.Id));
        Assert.True(await databaseContext.SecurityEvents.AnyAsync(row => row.Id == recentEvent.Id));
    }

    // L'application ne peut toujours pas effacer le journal elle-même : seule la fonction de la base le peut.
    [Fact]
    public async Task ApplicationStillCannotDeleteTheJournalItself()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();

        var refusal = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            databaseContext.Database.ExecuteSqlRawAsync("DELETE FROM evenement_securite"));

        Assert.Equal(Npgsql.PostgresErrorCodes.InsufficientPrivilege, refusal.SqlState);
    }

    // Les codes écrits dans la table sont ceux du dictionnaire de données, et l'adresse est une vraie adresse IP.
    [Theory]
    [InlineData("203.0.113.7")]
    [InlineData("2001:db8::7")]
    public async Task EventIsSavedWithTheCodesOfTheDataDictionaryAndItsIpAddress(string ipAddress)
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new SecurityEventRepository(databaseContext);
        var savedEvent = new SecurityEvent
        {
            Type = SecurityEventType.Login,
            Result = SecurityEventResult.Failure,
            OccurredAt = EventDate,
            IpAddress = IPAddress.Parse(ipAddress)
        };

        await repository.SaveEventAsync(savedEvent);

        var columns = await databaseContext.Database
            .SqlQuery<string>($"SELECT type_evenement || '/' || resultat_evenement || '/' || host(adresse_ip_evenement) AS \"Value\" FROM evenement_securite WHERE id_evenement = {savedEvent.Id}")
            .SingleAsync();
        Assert.Equal("connexion/echec/" + ipAddress, columns);
    }

    [Fact]
    public async Task LockoutEventIsSavedWithItsCodeAndItsResult()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new SecurityEventRepository(databaseContext);
        var savedEvent = new SecurityEvent
        {
            Type = SecurityEventType.Lockout,
            Result = SecurityEventResult.Success,
            OccurredAt = EventDate
        };

        await repository.SaveEventAsync(savedEvent);

        var columns = await databaseContext.Database
            .SqlQuery<string>($"SELECT type_evenement || '/' || resultat_evenement AS \"Value\" FROM evenement_securite WHERE id_evenement = {savedEvent.Id}")
            .SingleAsync();
        Assert.Equal("blocage/succes", columns);
    }

    // Une connexion avec une adresse inconnue n'a pas d'utilisateur, et le serveur de test n'a pas d'adresse IP.
    [Fact]
    public async Task EventWithoutUserAndWithoutIpAddressIsAccepted()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new SecurityEventRepository(databaseContext);
        var savedEvent = new SecurityEvent
        {
            Type = SecurityEventType.Login,
            Result = SecurityEventResult.Failure,
            OccurredAt = EventDate
        };

        await repository.SaveEventAsync(savedEvent);

        databaseContext.ChangeTracker.Clear();
        var reloadedEvent = await databaseContext.SecurityEvents.SingleAsync(row =>
            row.Id == savedEvent.Id);
        Assert.Null(reloadedEvent.UserId);
        Assert.Null(reloadedEvent.IpAddress);
        Assert.Equal(EventDate, reloadedEvent.OccurredAt);
    }

    // Un refus écrit tout de suite n'emporte jamais ce qui attend l'unité de travail : rien ne part, ni l'un ni l'autre.
    [Fact]
    public async Task ImmediateEventRefusesToCarryChangesWaitingForTheUnitOfWork()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new SecurityEventRepository(databaseContext);
        var preparedEvent = new SecurityEvent
        {
            Type = SecurityEventType.Login,
            Result = SecurityEventResult.Success,
            OccurredAt = EventDate
        };
        var refusalEvent = new SecurityEvent
        {
            Type = SecurityEventType.Login,
            Result = SecurityEventResult.Failure,
            OccurredAt = EventDate
        };
        repository.PrepareEvent(preparedEvent);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveEventAsync(refusalEvent));

        databaseContext.ChangeTracker.Clear();
        var savedEventCount = await databaseContext.SecurityEvents.CountAsync(row =>
            row.Id == preparedEvent.Id
            || row.Id == refusalEvent.Id);
        Assert.Equal(0, savedEventCount);
    }

    // Le compte d'un refus a pu être effacé entre-temps, par exemple par le nettoyage des comptes non confirmés.
    // L'événement est quand même gardé, comme ceux d'un compte supprimé : sans identifiant ni adresse IP (GR Suppression).
    [Fact]
    public async Task EventOfAnAccountErasedMeanwhileIsKeptWithoutIdentifierNorIpAddress()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new SecurityEventRepository(databaseContext);
        var savedEvent = new SecurityEvent
        {
            Type = SecurityEventType.Login,
            Result = SecurityEventResult.Failure,
            OccurredAt = EventDate,
            IpAddress = IPAddress.Parse("203.0.113.7"),
            UserId = Guid.NewGuid()
        };

        await repository.SaveEventAsync(savedEvent);

        databaseContext.ChangeTracker.Clear();
        var reloadedEvent = await databaseContext.SecurityEvents.SingleAsync(row =>
            row.Id == savedEvent.Id);
        Assert.Null(reloadedEvent.UserId);
        Assert.Null(reloadedEvent.IpAddress);
        Assert.Equal(SecurityEventType.Login, reloadedEvent.Type);
        Assert.Equal(SecurityEventResult.Failure, reloadedEvent.Result);
        Assert.Equal(EventDate, reloadedEvent.OccurredAt);
    }
}
