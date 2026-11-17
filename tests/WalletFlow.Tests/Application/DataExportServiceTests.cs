using WalletFlow.Application.DataExport;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Application.TwoFactorServicesWithFakeTools;

namespace WalletFlow.Tests.Application;

// Vérifie le téléchargement des données (GR Compte, SS §2, §6 et §11), avec de faux outils et la vraie limite en mémoire :
// le mot de passe et le code avant toute lecture, 3 essais par heure, le journal, et le code de secours dépensé une fois.
public class DataExportServiceTests
{
    private readonly TwoFactorServicesWithFakeTools tools = new TwoFactorServicesWithFakeTools();
    private readonly FakeDataExportRepository exportRepository = new FakeDataExportRepository();
    private readonly InMemoryDataExportLimiter limiter;

    public DataExportServiceTests()
    {
        limiter = new InMemoryDataExportLimiter(tools.Clock);
    }

    // Demande le fichier, avec ce mot de passe et ce code.
    private async Task<DataExportResult> ExportAsync(Guid userId, string password, string? code = null)
    {
        return await AccountSettingsServices.CreateDataExport(tools, exportRepository, limiter).ExportAsync(new DataExportAttempt
        {
            UserId = userId,
            Password = password,
            Code = code,
            IpAddress = IpAddress
        });
    }

    // Les événements du journal pour le téléchargement des données.
    private List<SecurityEvent> ExportEvents()
    {
        return tools.SecurityEventRepository.SavedSecurityEvents.Where(securityEvent => securityEvent.Type == SecurityEventType.DataExport).ToList();
    }

    // Le bon mot de passe donne les données du compte, lues après coup, et le téléchargement est noté au journal.
    [Fact]
    public async Task RightPasswordGivesTheDataOfTheAccountAndIsJournaled()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        var data = exportRepository.SaveDataFor(account.Id, AccountEmail);
        exportRepository.SaveDataFor(Guid.NewGuid(), "autre@exemple.test");

        var result = await ExportAsync(account.Id, AccountPassword);

        var exported = Assert.IsType<DataExportResult.Exported>(result);
        Assert.Same(data, exported.Data);
        Assert.Equal(tools.Clock.GetUtcNow().UtcDateTime, exported.ExportedAt);
        Assert.Equal(new List<Guid> { account.Id }, exportRepository.ReadUserIds);
        var journaled = Assert.Single(ExportEvents());
        Assert.Equal((SecurityEventResult.Success, (Guid?)account.Id, IpAddress), (journaled.Result, journaled.UserId, journaled.IpAddress));
    }

    // Un mauvais mot de passe ne lit rien, et le refus est noté au journal.
    [Fact]
    public async Task WrongPasswordReadsNothingAndIsJournaled()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        exportRepository.SaveDataFor(account.Id, AccountEmail);

        var result = await ExportAsync(account.Id, WrongPassword);

        Assert.IsType<DataExportResult.WrongPassword>(result);
        Assert.Empty(exportRepository.ReadUserIds);
        var journaled = Assert.Single(ExportEvents());
        Assert.Equal((SecurityEventResult.Failure, (Guid?)account.Id), (journaled.Result, journaled.UserId));
    }

    // Avec la double authentification, il faut aussi le code de l'application ; sans lui ou avec un mauvais, le refus est le même.
    [Fact]
    public async Task TwoFactorAccountNeedsTheCodeOfItsApplication()
    {
        var activatedAccount = tools.AddActivatedAccount();
        var accountId = activatedAccount.UserAccount.Id;
        exportRepository.SaveDataFor(accountId, AccountEmail);

        var withoutCode = await ExportAsync(accountId, AccountPassword);
        var wrongCode = await ExportAsync(accountId, AccountPassword, tools.FindWrongCode(activatedAccount.Secret));
        var rightCode = await ExportAsync(accountId, AccountPassword, tools.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<DataExportResult.WrongPasswordOrCode>(withoutCode);
        Assert.IsType<DataExportResult.WrongPasswordOrCode>(wrongCode);
        Assert.IsType<DataExportResult.Exported>(rightCode);
        Assert.Single(exportRepository.ReadUserIds);
        Assert.Equal(
            new List<SecurityEventResult> { SecurityEventResult.Failure, SecurityEventResult.Failure, SecurityEventResult.Success },
            ExportEvents().Select(securityEvent => securityEvent.Result).ToList());
    }

    // Avec la double authentification, un mauvais mot de passe reçoit le même refus qu'un mauvais code.
    [Fact]
    public async Task TwoFactorAccountWithWrongPasswordGetsTheSharedRefusal()
    {
        var activatedAccount = tools.AddActivatedAccount();

        var result = await ExportAsync(activatedAccount.UserAccount.Id, WrongPassword, tools.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<DataExportResult.WrongPasswordOrCode>(result);
        Assert.Empty(exportRepository.ReadUserIds);
    }

    // Un code de secours ne sert qu'une fois, et son alerte part avec le téléchargement (GR Notif).
    [Fact]
    public async Task BackupCodeServesOnlyOnceAndSendsItsAlert()
    {
        var activatedAccount = tools.AddActivatedAccount();
        var accountId = activatedAccount.UserAccount.Id;
        exportRepository.SaveDataFor(accountId, AccountEmail);

        var first = await ExportAsync(accountId, AccountPassword, activatedAccount.BackupCodes[0]);
        var second = await ExportAsync(accountId, AccountPassword, activatedAccount.BackupCodes[0]);

        Assert.IsType<DataExportResult.Exported>(first);
        Assert.IsType<DataExportResult.WrongPasswordOrCode>(second);
        Assert.Single(tools.TwoFactorRepository.SavedBackupCodes, savedCode => savedCode.UsedAt != null);
        var alert = Assert.Single(tools.EmailRequests.SavedRequests);
        Assert.Equal((EmailRequestType.BackupCodeUse, (Guid?)accountId), (alert.Type, alert.UserId));
    }

    // Le code de secours n'est dépensé que si le téléchargement est noté : si l'enregistrement échoue, il reste valable, et rien
    // n'est lu.
    [Fact]
    public async Task BackupCodeIsNotSpentWhenTheExportCannotBeRecorded()
    {
        var activatedAccount = tools.AddActivatedAccount();
        exportRepository.SaveDataFor(activatedAccount.UserAccount.Id, AccountEmail);
        tools.EmailRequests.IsDown = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ExportAsync(activatedAccount.UserAccount.Id, AccountPassword, activatedAccount.BackupCodes[0]));

        Assert.DoesNotContain(tools.TwoFactorRepository.SavedBackupCodes, savedCode => savedCode.UsedAt != null);
        Assert.Empty(exportRepository.ReadUserIds);
        Assert.Empty(ExportEvents());
    }

    // Un mot de passe changé entre la vérification et l'usage du code de secours arrête tout : le code reste valable.
    [Fact]
    public async Task PasswordChangedMeanwhileStopsTheBackupCodeUse()
    {
        var activatedAccount = tools.AddActivatedAccount();
        exportRepository.SaveDataFor(activatedAccount.UserAccount.Id, AccountEmail);
        tools.PasswordHasher.OnPasswordVerified = () => activatedAccount.UserAccount.ReplacePasswordHash("empreinte-du-nouveau-mot-de-passe");

        var result = await ExportAsync(activatedAccount.UserAccount.Id, AccountPassword, activatedAccount.BackupCodes[0]);

        Assert.IsType<DataExportResult.WrongPasswordOrCode>(result);
        Assert.DoesNotContain(tools.TwoFactorRepository.SavedBackupCodes, savedCode => savedCode.UsedAt != null);
        Assert.Empty(exportRepository.ReadUserIds);
    }

    // Un mot de passe changé juste avant l'enregistrement, par une récupération au même instant, est vu grâce au compte contrôlé
    // à l'enregistrement : le passage suivant relit le compte, et le téléchargement est refusé. Le code de secours reste valable.
    [Fact]
    public async Task PasswordChangedJustBeforeTheSaveStopsTheBackupCodeUse()
    {
        var activatedAccount = tools.AddActivatedAccount();
        exportRepository.SaveDataFor(activatedAccount.UserAccount.Id, AccountEmail);
        tools.AccountRepository.PasswordChangedBeforeCheckedSave = "empreinte-changee-entre-temps";

        var result = await ExportAsync(activatedAccount.UserAccount.Id, AccountPassword, activatedAccount.BackupCodes[0]);

        Assert.IsType<DataExportResult.WrongPasswordOrCode>(result);
        Assert.DoesNotContain(tools.TwoFactorRepository.SavedBackupCodes, savedCode => savedCode.UsedAt != null);
        Assert.Empty(exportRepository.ReadUserIds);
    }

    // Un secret de l'application changé pendant la vérification arrête tout : le code de secours a été vérifié pour une autre
    // activation de la double authentification. Il reste valable, et rien n'est lu.
    [Fact]
    public async Task AuthenticatorSecretChangedMeanwhileStopsTheBackupCodeUse()
    {
        var activatedAccount = tools.AddActivatedAccount();
        exportRepository.SaveDataFor(activatedAccount.UserAccount.Id, AccountEmail);
        tools.PasswordHasher.OnPasswordVerified = () => activatedAccount.UserAccount.ActivateTwoFactor("autre-secret-chiffre");

        var result = await ExportAsync(activatedAccount.UserAccount.Id, AccountPassword, activatedAccount.BackupCodes[0]);

        Assert.IsType<DataExportResult.WrongPasswordOrCode>(result);
        Assert.DoesNotContain(tools.TwoFactorRepository.SavedBackupCodes, savedCode => savedCode.UsedAt != null);
        Assert.Empty(exportRepository.ReadUserIds);
    }

    // SS §6 : 3 essais par heure, bons ou mauvais ; le 4e est refusé avant même le mot de passe. Une session volée ne peut donc
    // pas essayer plus de 3 mots de passe par heure. Une heure après le premier essai, le compteur repart.
    [Fact]
    public async Task EveryTryCountsInTheLimitOfThreePerHour()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        exportRepository.SaveDataFor(account.Id, AccountEmail);

        Assert.IsType<DataExportResult.WrongPassword>(await ExportAsync(account.Id, WrongPassword));
        Assert.IsType<DataExportResult.WrongPassword>(await ExportAsync(account.Id, WrongPassword));
        Assert.IsType<DataExportResult.Exported>(await ExportAsync(account.Id, AccountPassword));
        var verificationsBefore = tools.PasswordHasher.VerifiedPasswordCount;

        var fourth = await ExportAsync(account.Id, AccountPassword);
        tools.Clock.Advance(TimeSpan.FromHours(1));
        var anHourLater = await ExportAsync(account.Id, AccountPassword);

        Assert.IsType<DataExportResult.TooManyExports>(fourth);
        Assert.Equal(verificationsBefore + 1, tools.PasswordHasher.VerifiedPasswordCount);
        Assert.IsType<DataExportResult.Exported>(anHourLater);
    }

    // Un compte effacé avant la vérification, ou entre la vérification et la lecture, n'a plus de données à donner.
    [Fact]
    public async Task ErasedAccountHasNoDataToGive()
    {
        var unknown = await ExportAsync(Guid.NewGuid(), AccountPassword);
        var account = tools.AddAccountWithoutTwoFactor();
        var erasedBeforeTheRead = await ExportAsync(account.Id, AccountPassword);

        Assert.IsType<DataExportResult.AccountGone>(unknown);
        Assert.IsType<DataExportResult.AccountGone>(erasedBeforeTheRead);
        Assert.Equal(new List<Guid> { account.Id }, exportRepository.ReadUserIds);
    }
}
