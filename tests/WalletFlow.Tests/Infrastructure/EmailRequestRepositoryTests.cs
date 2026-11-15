using System.Net;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Emails;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la file d'e-mails sur la vraie base (DD §9.4, SS §9). Chaque test annule sa transaction à la fin : la base reste vide.
// Les dates de ces tests sont en 2001 : une demande validée par un autre test, toujours plus récente, n'est jamais à leur heure.
public class EmailRequestRepositoryTests
{
    private static readonly DateTime Now = new DateTime(2001, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IPAddress IpAddress = IPAddress.Parse("2001:db8::7");

    // Fait entrer ces demandes dans la vraie file, comme un service, puis oublie les objets gardés en mémoire.
    private static async Task SaveAsync(WalletFlowDatabaseContext databaseContext, EmailRequestRepository repository, params EmailRequest[] requests)
    {
        foreach (var request in requests)
        {
            repository.PrepareRequest(request);
        }

        Assert.True(await TrySaveChangesAsync(databaseContext));
        databaseContext.ChangeTracker.Clear();
    }

    // Lit le code enregistré pour le type de cette demande.
    private static async Task<string> ReadTypeCodeAsync(WalletFlowDatabaseContext databaseContext, Guid requestId)
    {
        return await databaseContext.Database
            .SqlQuery<string>($"SELECT type_demande_email AS \"Value\" FROM demande_email WHERE id_demande_email = {requestId}")
            .SingleAsync();
    }

    // Chaque type d'e-mail a le code du dictionnaire de données, et chaque valeur revient telle quelle.
    [Fact]
    public async Task EachRequestIsSavedWithItsDatabaseCodeAndReadBackUnchanged()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveConfirmedAccountAsync(databaseContext, "file-codes@exemple.test");
        var repository = new EmailRequestRepository(databaseContext, new EmailRequestWakeUp());
        var requestsAndCodes = new List<(EmailRequest Request, string Code)>
        {
            (EmailRequest.ForPasswordRecovery("file-codes@exemple.test", IpAddress, Now), "recuperation_mdp"),
            (EmailRequest.ForAccountUnlock(account.Id, IpAddress, Now), "deblocage"),
            (EmailRequest.ForEmailChange(account.Id, "nouvelle-file@exemple.test", IpAddress, Now), "changement_email"),
            (EmailRequest.ForEmailChangedNotice(account.Id, "ancienne-file@exemple.test", IpAddress, Now), "avis_ancienne_adresse"),
            (EmailRequest.ForNewDevice(account.Id, IpAddress, Now), "nouvel_appareil"),
            (EmailRequest.ForPasswordChange(account.Id, null, Now), "changement_mdp")
        };

        await SaveAsync(databaseContext, repository, requestsAndCodes.Select(pair => pair.Request).ToArray());

        foreach (var (request, code) in requestsAndCodes)
        {
            Assert.Equal(code, await ReadTypeCodeAsync(databaseContext, request.Id));

            var savedRequest = await repository.FindAsync(request.Id);
            Assert.NotNull(savedRequest);
            Assert.Equal(request.Type, savedRequest.Type);
            Assert.Equal(request.EmailAddress, savedRequest.EmailAddress);
            Assert.Equal(request.IpAddress, savedRequest.IpAddress);
            Assert.Equal(request.UserId, savedRequest.UserId);
            Assert.Equal(Now, savedRequest.RequestedAt);
            Assert.Equal(Now, savedRequest.NextTryAt);
            Assert.Equal(0, savedRequest.FailedTryCount);
            Assert.Null(savedRequest.AlertId);
        }
    }

    // L'essai le plus en retard passe en premier. Une demande dont l'heure est exactement maintenant est déjà due,
    // et une demande prévue une microseconde plus tard attend.
    [Fact]
    public async Task NextDueRequestIsTheOneWaitingTheLongest()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveConfirmedAccountAsync(databaseContext, "file-ordre@exemple.test");
        var repository = new EmailRequestRepository(databaseContext, new EmailRequestWakeUp());
        var later = EmailRequest.ForNewDevice(account.Id, IpAddress, Now.AddTicks(10));
        var exactlyNow = EmailRequest.ForNewDevice(account.Id, IpAddress, Now);
        var oldest = EmailRequest.ForNewDevice(account.Id, IpAddress, Now.AddMinutes(-1));
        await SaveAsync(databaseContext, repository, later, exactlyNow, oldest);

        Assert.Equal(oldest.Id, await repository.FindNextDueIdAsync(Now));

        repository.PrepareRemoval(Assert.IsType<EmailRequest>(await repository.FindAsync(oldest.Id)));
        Assert.True(await TrySaveChangesAsync(databaseContext));
        Assert.Equal(exactlyNow.Id, await repository.FindNextDueIdAsync(Now));

        repository.PrepareRemoval(Assert.IsType<EmailRequest>(await repository.FindAsync(exactlyNow.Id)));
        Assert.True(await TrySaveChangesAsync(databaseContext));
        Assert.Null(await repository.FindNextDueIdAsync(Now));
        Assert.Equal(later.Id, await repository.FindNextDueIdAsync(Now.AddTicks(10)));
    }

    // Seul l'enregistrement réussi d'une nouvelle demande réveille la tâche de fond : ni un autre enregistrement,
    // ni un enregistrement que la base refuse. Une demande d'un autre type que la récupération vise toujours un compte.
    [Fact]
    public async Task OnlyASavedNewRequestWakesTheBackgroundTaskUp()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var wakeUp = new EmailRequestWakeUp();
        var repository = new EmailRequestRepository(databaseContext, wakeUp);
        var account = await SaveConfirmedAccountAsync(databaseContext, "file-reveil@exemple.test");
        Assert.False(await wakeUp.WaitAsync(TimeSpan.Zero, CancellationToken.None));

        var request = EmailRequest.ForPasswordChange(account.Id, IpAddress, Now);
        await SaveAsync(databaseContext, repository, request);
        Assert.True(await wakeUp.WaitAsync(TimeSpan.Zero, CancellationToken.None));

        var savedRequest = Assert.IsType<EmailRequest>(await repository.FindAsync(request.Id));
        Assert.True(savedRequest.RecordFailedTry(Now));
        Assert.True(await TrySaveChangesAsync(databaseContext));
        Assert.False(await wakeUp.WaitAsync(TimeSpan.Zero, CancellationToken.None));

        repository.PrepareRequest(new EmailRequest { Type = EmailRequestType.NewDevice, RequestedAt = Now, NextTryAt = Now });
        await Assert.ThrowsAsync<DbUpdateException>(() => TrySaveChangesAsync(databaseContext));
        Assert.False(await wakeUp.WaitAsync(TimeSpan.Zero, CancellationToken.None));
    }
}
