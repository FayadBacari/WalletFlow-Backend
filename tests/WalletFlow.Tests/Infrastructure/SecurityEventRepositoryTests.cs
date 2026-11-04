using System.Net;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.LocalDatabaseForTheTests;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le journal de sécurité dans la vraie base PostgreSQL locale (SS §11).
// Chaque test travaille dans une transaction annulée à la fin : la base reste vide.
public class SecurityEventRepositoryTests
{
    private static readonly DateTime DateOfTheEventInTheseTests = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    // Les codes écrits dans la table sont ceux du dictionnaire de données, et l'adresse est une vraie adresse IP.
    [Theory]
    [InlineData("203.0.113.7")]
    [InlineData("2001:db8::7")]
    public async Task EventIsSavedWithTheCodesOfTheDataDictionaryAndItsIpAddress(string ipAddressOfTheEventInThisTest)
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new SecurityEventRepository(databaseContextOfThisRepositoryTest);
        var securityEventSavedByThisTest = new SecurityEvent
        {
            Type = SecurityEventType.Login,
            Result = SecurityEventResult.Failure,
            OccurredAt = DateOfTheEventInTheseTests,
            IpAddress = IPAddress.Parse(ipAddressOfTheEventInThisTest)
        };

        await repositoryBeingTestedOnTheRealDatabase.SaveSecurityEventInTheJournalAsync(securityEventSavedByThisTest);

        var columnsReadDirectlyFromTheTable = await databaseContextOfThisRepositoryTest.Database
            .SqlQuery<string>($"SELECT type_evenement || '/' || resultat_evenement || '/' || host(adresse_ip_evenement) AS \"Value\" FROM evenement_securite WHERE id_evenement = {securityEventSavedByThisTest.Id}")
            .SingleAsync();
        Assert.Equal("connexion/echec/" + ipAddressOfTheEventInThisTest, columnsReadDirectlyFromTheTable);
    }

    [Fact]
    public async Task LockoutEventIsSavedWithItsCodeAndItsResult()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new SecurityEventRepository(databaseContextOfThisRepositoryTest);
        var securityEventSavedByThisTest = new SecurityEvent
        {
            Type = SecurityEventType.Lockout,
            Result = SecurityEventResult.Success,
            OccurredAt = DateOfTheEventInTheseTests
        };

        await repositoryBeingTestedOnTheRealDatabase.SaveSecurityEventInTheJournalAsync(securityEventSavedByThisTest);

        var columnsReadDirectlyFromTheTable = await databaseContextOfThisRepositoryTest.Database
            .SqlQuery<string>($"SELECT type_evenement || '/' || resultat_evenement AS \"Value\" FROM evenement_securite WHERE id_evenement = {securityEventSavedByThisTest.Id}")
            .SingleAsync();
        Assert.Equal("blocage/succes", columnsReadDirectlyFromTheTable);
    }

    // Une connexion avec une adresse inconnue n'a pas d'utilisateur, et le serveur de test n'a pas d'adresse IP.
    [Fact]
    public async Task EventWithoutUserAndWithoutIpAddressIsAccepted()
    {
        await using var databaseContextOfThisRepositoryTest = CreateDatabaseContextConnectedToLocalDatabase();
        await using var transactionCancelledAtTheEndOfThisTest = await databaseContextOfThisRepositoryTest.Database.BeginTransactionAsync();
        var repositoryBeingTestedOnTheRealDatabase = new SecurityEventRepository(databaseContextOfThisRepositoryTest);
        var securityEventSavedByThisTest = new SecurityEvent
        {
            Type = SecurityEventType.Login,
            Result = SecurityEventResult.Failure,
            OccurredAt = DateOfTheEventInTheseTests
        };

        await repositoryBeingTestedOnTheRealDatabase.SaveSecurityEventInTheJournalAsync(securityEventSavedByThisTest);

        databaseContextOfThisRepositoryTest.ChangeTracker.Clear();
        var eventReadBackFromTheRealDatabase = await databaseContextOfThisRepositoryTest.SecurityEvents.SingleAsync(eventSearchedInTheDatabaseTable =>
            eventSearchedInTheDatabaseTable.Id == securityEventSavedByThisTest.Id);
        Assert.Null(eventReadBackFromTheRealDatabase.UserId);
        Assert.Null(eventReadBackFromTheRealDatabase.IpAddress);
        Assert.Equal(DateOfTheEventInTheseTests, eventReadBackFromTheRealDatabase.OccurredAt);
    }
}
