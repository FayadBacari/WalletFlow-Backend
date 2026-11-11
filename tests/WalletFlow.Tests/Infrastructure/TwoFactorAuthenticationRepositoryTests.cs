using System.Net;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la double authentification dans la vraie base PostgreSQL locale, avec l'utilisateur de l'API.
// Chaque test travaille dans une transaction annulée à la fin : la base reste vide.
public class TwoFactorAuthenticationRepositoryTests
{
    private const string Secret = "secret-chiffre-de-ces-tests";

    private static readonly DateTime Now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    // Enregistre un nouveau compte confirmé avec cette adresse, puis oublie les objets gardés en mémoire.
    private static async Task<User> SaveConfirmedAccountAsync(WalletFlowDatabaseContext databaseContext, string emailAddress)
    {
        var newAccount = new User
        {
            Email = emailAddress,
            PasswordHash = "empreinte-de-test",
            IsEmailConfirmed = true
        };
        databaseContext.Users.Add(newAccount);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        return newAccount;
    }

    // Crée ce nombre de codes de secours, sans les enregistrer.
    private static List<BackupCode> CreateBackupCodes(Guid accountId, int backupCodeCount)
    {
        var backupCodes = new List<BackupCode>();

        for (var codeNumber = 1; codeNumber <= backupCodeCount; codeNumber++)
        {
            backupCodes.Add(new BackupCode { CodeHash = "empreinte-du-code-" + codeNumber, UserId = accountId });
        }

        return backupCodes;
    }

    // Crée un événement du journal, sans l'enregistrer.
    private static SecurityEvent CreateSecurityEvent(Guid accountId, SecurityEventType eventType)
    {
        return new SecurityEvent
        {
            Type = eventType,
            Result = SecurityEventResult.Success,
            OccurredAt = Now,
            IpAddress = IPAddress.Parse("203.0.113.7"),
            UserId = accountId
        };
    }

    // Prépare une activation comme le fait le cas d'utilisation : secret chiffré, codes de secours et événement. Rien n'est encore écrit.
    private static void PrepareActivation(
        WalletFlowDatabaseContext databaseContext,
        User account,
        List<BackupCode> backupCodes,
        SecurityEvent securityEvent)
    {
        account.ActivateTwoFactor(Secret);
        new TwoFactorAuthenticationRepository(databaseContext).PrepareBackupCodes(backupCodes);
        new SecurityEventRepository(databaseContext).PrepareEvent(securityEvent);
    }

    // Prépare une désactivation comme le fait le cas d'utilisation : secret oublié, codes de secours effacés et événement.
    private static async Task PrepareDeactivationAsync(
        WalletFlowDatabaseContext databaseContext,
        User account,
        SecurityEvent securityEvent)
    {
        account.DeactivateTwoFactor();
        await new TwoFactorAuthenticationRepository(databaseContext).PrepareBackupCodesRemovalAsync(account.Id);
        new SecurityEventRepository(databaseContext).PrepareEvent(securityEvent);
    }

    // Prépare une connexion réussie comme le fait le cas d'utilisation, avec le code de secours utilisé s'il y en a un.
    private static void PrepareBackupCodeLogin(
        WalletFlowDatabaseContext databaseContext,
        User account,
        UserSession session,
        BackupCode? backupCode,
        SecurityEvent securityEvent)
    {
        if (backupCode != null)
        {
            backupCode.MarkAsUsed(session.LastActivityAt);
        }

        account.ResetLoginCounters();
        new UserAccountRepository(databaseContext).PrepareUnchangedCheck(account);
        new UserSessionRepository(databaseContext).PrepareSession(session);
        new SecurityEventRepository(databaseContext).PrepareEvent(securityEvent);
    }

    // Active un compte directement, comme le ferait une activation déjà réussie, puis oublie les objets gardés en mémoire.
    private static async Task ActivateDirectlyInDatabaseAsync(WalletFlowDatabaseContext databaseContext, Guid accountId, int backupCodeCount)
    {
        var account = await databaseContext.Users.SingleAsync(row => row.Id == accountId);
        PrepareActivation(
            databaseContext,
            account,
            CreateBackupCodes(accountId, backupCodeCount),
            CreateSecurityEvent(accountId, SecurityEventType.TwoFactorActivation));
        Assert.True(await TrySaveChangesAsync(databaseContext));
        databaseContext.ChangeTracker.Clear();
    }

    // Une autre requête change le compteur directement en base : les objets gardés en mémoire ne le voient pas.
    private static async Task SimulateConcurrentAttemptAsync(WalletFlowDatabaseContext databaseContext, Guid accountId)
    {
        await WriteAsAnotherRequestAsync(databaseContext, () => databaseContext.Users
            .Where(row => row.Id == accountId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                row => row.FailedLoginAttemptCount,
                (short)1)));
    }

    // Une autre requête enregistre son activation directement en base : les objets gardés en mémoire ne le voient pas.
    private static async Task SimulateConcurrentActivationAsync(WalletFlowDatabaseContext databaseContext, Guid accountId)
    {
        await WriteAsAnotherRequestAsync(databaseContext, () => databaseContext.Users
            .Where(row => row.Id == accountId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.IsTwoFactorEnabled, true)
                .SetProperty(row => row.EncryptedTotpSecret, "secret-de-l-autre-requete")));
    }

    // Compte les événements de ce type dans le vrai journal, pour ce compte.
    private static async Task<int> CountSecurityEventsAsync(WalletFlowDatabaseContext databaseContext, Guid accountId, SecurityEventType type)
    {
        return await databaseContext.SecurityEvents.CountAsync(row =>
            row.UserId == accountId
            && row.Type == type);
    }

    // Vérifie que la base n'a rien gardé de l'activation pour ce compte, à part ce qu'un test y a écrit lui-même.
    private static async Task<int> CountBackupCodesAsync(WalletFlowDatabaseContext databaseContext, Guid accountId)
    {
        return await databaseContext.BackupCodes.CountAsync(row => row.UserId == accountId);
    }

    // --- L'activation

    // La contrainte ck_utilisateur_totp du MPD refuserait un compte activé sans secret : ce test passe par le vrai script.
    [Fact]
    public async Task ActivationIsReallySavedWithItsSecretItsCodesAndItsEvent()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-activation-reussie@exemple.test");
        var account = await databaseContext.Users.SingleAsync(row => row.Id == savedAccount.Id);

        PrepareActivation(
            databaseContext,
            account,
            CreateBackupCodes(savedAccount.Id, 10),
            CreateSecurityEvent(savedAccount.Id, SecurityEventType.TwoFactorActivation));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.True(saved);

        // On oublie les objets en mémoire : tout ce qui suit est vraiment relu dans la base.
        databaseContext.ChangeTracker.Clear();
        var reloadedAccount = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Id == savedAccount.Id);
        Assert.True(reloadedAccount.IsTwoFactorEnabled);
        Assert.Equal(Secret, reloadedAccount.EncryptedTotpSecret);
        Assert.Equal(10, await CountBackupCodesAsync(databaseContext, savedAccount.Id));
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, savedAccount.Id, SecurityEventType.TwoFactorActivation));
    }

    // Tout ou rien : un conflit n'écrit ni le secret, ni les codes, ni l'événement. Le même enregistrement réussit ensuite, une fois le compte relu.
    [Fact]
    public async Task ConflictOnTheAccountSavesNothingOfTheActivationAndItCanBeSavedAfterwards()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-activation-conflit@exemple.test");
        var account = await databaseContext.Users.SingleAsync(row => row.Id == savedAccount.Id);
        await SimulateConcurrentAttemptAsync(databaseContext, savedAccount.Id);
        var backupCodes = CreateBackupCodes(savedAccount.Id, 10);
        var securityEvent = CreateSecurityEvent(savedAccount.Id, SecurityEventType.TwoFactorActivation);

        PrepareActivation(databaseContext, account, backupCodes, securityEvent);
        var firstSaved = await TrySaveChangesAsync(databaseContext);

        Assert.False(firstSaved);
        Assert.Equal(0, await CountBackupCodesAsync(databaseContext, savedAccount.Id));
        Assert.Equal(0, await CountSecurityEventsAsync(databaseContext, savedAccount.Id, SecurityEventType.TwoFactorActivation));
        var reloadedAccount = await databaseContext.Users.SingleAsync(row => row.Id == savedAccount.Id);
        Assert.False(reloadedAccount.IsTwoFactorEnabled);
        Assert.Null(reloadedAccount.EncryptedTotpSecret);

        PrepareActivation(databaseContext, reloadedAccount, backupCodes, securityEvent);
        var secondSaved = await TrySaveChangesAsync(databaseContext);

        Assert.True(secondSaved);
        Assert.Equal(10, await CountBackupCodesAsync(databaseContext, savedAccount.Id));
    }

    // Deux activations simultanées : la seconde ne remplace pas le secret de la première, et le compte n'a jamais 20 codes.
    [Fact]
    public async Task SecondActivationAtTheSameMomentSavesNothing()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-activation-double@exemple.test");
        var account = await databaseContext.Users.SingleAsync(row => row.Id == savedAccount.Id);
        await SimulateConcurrentActivationAsync(databaseContext, savedAccount.Id);

        PrepareActivation(
            databaseContext,
            account,
            CreateBackupCodes(savedAccount.Id, 10),
            CreateSecurityEvent(savedAccount.Id, SecurityEventType.TwoFactorActivation));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.False(saved);
        Assert.Equal(0, await CountBackupCodesAsync(databaseContext, savedAccount.Id));
        var reloadedAccount = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Id == savedAccount.Id);
        Assert.Equal("secret-de-l-autre-requete", reloadedAccount.EncryptedTotpSecret);
    }

    // --- La lecture des codes

    [Fact]
    public async Task OnlyUnusedBackupCodesOfThisAccountAreFound()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new TwoFactorAuthenticationRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-codes-inutilises@exemple.test");
        var otherAccount = await SaveConfirmedAccountAsync(databaseContext, "test-codes-autre-compte@exemple.test");
        await ActivateDirectlyInDatabaseAsync(databaseContext, savedAccount.Id, 3);
        await ActivateDirectlyInDatabaseAsync(databaseContext, otherAccount.Id, 2);
        var backupCode = await databaseContext.BackupCodes.FirstAsync(row => row.UserId == savedAccount.Id);
        backupCode.MarkAsUsed(Now);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        var unusedBackupCodes = await repository.FindUnusedBackupCodesAsync(savedAccount.Id);

        Assert.Equal(2, unusedBackupCodes.Count);
        foreach (var unusedBackupCode in unusedBackupCodes)
        {
            Assert.Equal(savedAccount.Id, unusedBackupCode.UserId);
            Assert.NotEqual(backupCode.Id, unusedBackupCode.Id);
            Assert.Null(unusedBackupCode.UsedAt);
        }

        // Cette lecture ne sert qu'à comparer : EF Core ne doit pas suivre ces lignes.
        Assert.Empty(databaseContext.ChangeTracker.Entries<BackupCode>());
    }

    [Fact]
    public async Task BackupCodeIsFoundByItsIdentifierAndUnknownIdentifierFindsNothing()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new TwoFactorAuthenticationRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-code-par-identifiant@exemple.test");
        await ActivateDirectlyInDatabaseAsync(databaseContext, savedAccount.Id, 1);
        var backupCode = await databaseContext.BackupCodes.AsNoTracking().SingleAsync(row => row.UserId == savedAccount.Id);

        Assert.Equal(backupCode.CodeHash, (await repository.FindBackupCodeAsync(backupCode.Id))!.CodeHash);
        Assert.Null(await repository.FindBackupCodeAsync(Guid.NewGuid()));
    }

    // --- La connexion avec un code de secours

    [Fact]
    public async Task LoginWithABackupCodeReallyMarksItAsUsedAtTheDateOfTheLogin()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-connexion-code-secours@exemple.test");
        await ActivateDirectlyInDatabaseAsync(databaseContext, savedAccount.Id, 2);
        var account = await databaseContext.Users.SingleAsync(row => row.Id == savedAccount.Id);
        var backupCode = await databaseContext.BackupCodes.FirstAsync(row => row.UserId == savedAccount.Id);

        PrepareBackupCodeLogin(
            databaseContext,
            account,
            new UserSession { Id = new string('c', 64), LastActivityAt = Now, UserId = savedAccount.Id },
            backupCode,
            CreateSecurityEvent(savedAccount.Id, SecurityEventType.Login));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.True(saved);
        databaseContext.ChangeTracker.Clear();
        var reloadedBackupCode = await databaseContext.BackupCodes.AsNoTracking().SingleAsync(row => row.Id == backupCode.Id);
        Assert.Equal(Now, reloadedBackupCode.UsedAt);
        Assert.Single(await new TwoFactorAuthenticationRepository(databaseContext).FindUnusedBackupCodesAsync(savedAccount.Id));
    }

    // Deux requêtes avec le même code : celle qui écrit en second n'ouvre aucune session.
    [Fact]
    public async Task BackupCodeUsedByAnotherRequestJustBeforeTheSaveOpensNoSession()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-code-secours-course@exemple.test");
        await ActivateDirectlyInDatabaseAsync(databaseContext, savedAccount.Id, 1);
        var account = await databaseContext.Users.SingleAsync(row => row.Id == savedAccount.Id);
        var backupCode = await databaseContext.BackupCodes.SingleAsync(row => row.UserId == savedAccount.Id);

        await WriteAsAnotherRequestAsync(databaseContext, () => databaseContext.BackupCodes
            .Where(row => row.Id == backupCode.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                row => row.UsedAt,
                Now)));

        PrepareBackupCodeLogin(
            databaseContext,
            account,
            new UserSession { Id = new string('d', 64), LastActivityAt = Now.AddSeconds(1), UserId = savedAccount.Id },
            backupCode,
            CreateSecurityEvent(savedAccount.Id, SecurityEventType.Login));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.False(saved);
        Assert.Equal(0, await databaseContext.UserSessions.CountAsync(row => row.UserId == savedAccount.Id));
    }

    // Une connexion lancée sans code ne peut pas s'enregistrer si la double authentification vient d'être activée.
    [Fact]
    public async Task LoginReadBeforeAnActivationOpensNoSession()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-connexion-avant-activation@exemple.test");
        var account = await databaseContext.Users.SingleAsync(row => row.Id == savedAccount.Id);
        await SimulateConcurrentActivationAsync(databaseContext, savedAccount.Id);

        PrepareBackupCodeLogin(
            databaseContext,
            account,
            new UserSession { Id = new string('e', 64), LastActivityAt = Now, UserId = savedAccount.Id },
            null,
            CreateSecurityEvent(savedAccount.Id, SecurityEventType.Login));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.False(saved);
        Assert.Equal(0, await databaseContext.UserSessions.CountAsync(row => row.UserId == savedAccount.Id));
    }

    // --- La désactivation

    [Fact]
    public async Task DeactivationReallyErasesTheSecretAndEveryBackupCodeOfThisAccountOnly()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-desactivation@exemple.test");
        var otherAccount = await SaveConfirmedAccountAsync(databaseContext, "test-desactivation-autre@exemple.test");
        await ActivateDirectlyInDatabaseAsync(databaseContext, savedAccount.Id, 10);
        await ActivateDirectlyInDatabaseAsync(databaseContext, otherAccount.Id, 10);
        var backupCode = await databaseContext.BackupCodes.FirstAsync(row => row.UserId == savedAccount.Id);
        backupCode.MarkAsUsed(Now);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();
        var account = await databaseContext.Users.SingleAsync(row => row.Id == savedAccount.Id);

        await PrepareDeactivationAsync(databaseContext, account, CreateSecurityEvent(savedAccount.Id, SecurityEventType.TwoFactorDeactivation));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.True(saved);
        databaseContext.ChangeTracker.Clear();
        var reloadedAccount = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Id == savedAccount.Id);
        Assert.False(reloadedAccount.IsTwoFactorEnabled);
        Assert.Null(reloadedAccount.EncryptedTotpSecret);
        Assert.Equal(0, await CountBackupCodesAsync(databaseContext, savedAccount.Id));
        Assert.Equal(10, await CountBackupCodesAsync(databaseContext, otherAccount.Id));
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, savedAccount.Id, SecurityEventType.TwoFactorDeactivation));
    }

    // Tout ou rien : un conflit ne touche ni le secret, ni les codes, ni le journal.
    [Fact]
    public async Task ConflictOnTheAccountSavesNothingOfTheDeactivation()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-desactivation-conflit@exemple.test");
        await ActivateDirectlyInDatabaseAsync(databaseContext, savedAccount.Id, 10);
        var account = await databaseContext.Users.SingleAsync(row => row.Id == savedAccount.Id);
        await SimulateConcurrentAttemptAsync(databaseContext, savedAccount.Id);

        await PrepareDeactivationAsync(databaseContext, account, CreateSecurityEvent(savedAccount.Id, SecurityEventType.TwoFactorDeactivation));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.False(saved);
        var reloadedAccount = await databaseContext.Users.AsNoTracking().SingleAsync(row => row.Id == savedAccount.Id);
        Assert.True(reloadedAccount.IsTwoFactorEnabled);
        Assert.Equal(Secret, reloadedAccount.EncryptedTotpSecret);
        Assert.Equal(10, await CountBackupCodesAsync(databaseContext, savedAccount.Id));
        Assert.Equal(0, await CountSecurityEventsAsync(databaseContext, savedAccount.Id, SecurityEventType.TwoFactorDeactivation));
    }

    // Une désactivation qui voit un code utilisé à l'instant recommence : elle n'efface jamais une ligne qu'elle n'a pas relue.
    [Fact]
    public async Task BackupCodeUsedByAnotherRequestJustBeforeTheDeactivationSavesNothing()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-desactivation-code@exemple.test");
        await ActivateDirectlyInDatabaseAsync(databaseContext, savedAccount.Id, 1);
        var account = await databaseContext.Users.SingleAsync(row => row.Id == savedAccount.Id);
        var trackedCode = await databaseContext.BackupCodes.SingleAsync(row => row.UserId == savedAccount.Id);
        await WriteAsAnotherRequestAsync(databaseContext, () => databaseContext.BackupCodes
            .Where(row => row.Id == trackedCode.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                row => row.UsedAt,
                Now)));

        await PrepareDeactivationAsync(databaseContext, account, CreateSecurityEvent(savedAccount.Id, SecurityEventType.TwoFactorDeactivation));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.False(saved);
        Assert.Equal(1, await CountBackupCodesAsync(databaseContext, savedAccount.Id));
    }

    // Le journal accepte le nouveau type d'événement : c'est le contrôle ck_evenement_type de la vraie base qui répond.
    [Fact]
    public async Task DeactivationEventIsAcceptedByTheRealJournal()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-journal-desactivation@exemple.test");

        await new SecurityEventRepository(databaseContext).SaveEventAsync(CreateSecurityEvent(savedAccount.Id, SecurityEventType.TwoFactorDeactivation));

        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, savedAccount.Id, SecurityEventType.TwoFactorDeactivation));
    }
}
