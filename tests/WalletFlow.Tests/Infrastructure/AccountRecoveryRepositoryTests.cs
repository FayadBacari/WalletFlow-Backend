using System.Net;
using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les liens de récupération et de déblocage dans la vraie base PostgreSQL locale, avec l'utilisateur de l'API.
// Chaque test travaille dans une transaction annulée à la fin : la base reste vide.
public class AccountRecoveryRepositoryTests
{
    private const string OldHash = "ancienne-empreinte-du-compte";

    private static readonly DateTime Now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    // Enregistre un nouveau compte confirmé avec cette adresse, puis oublie les objets gardés en mémoire.
    private static async Task<User> SaveConfirmedAccountAsync(WalletFlowDatabaseContext databaseContext, string emailAddress)
    {
        var newAccount = new User { Email = emailAddress, PasswordHash = OldHash, IsEmailConfirmed = true };
        databaseContext.Users.Add(newAccount);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        return newAccount;
    }

    // Crée un jeton avec une empreinte unique, sans l'enregistrer.
    private static EmailToken CreateEmailToken(User account, EmailTokenType tokenType)
    {
        return new EmailToken
        {
            Type = tokenType,
            TokenHash = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + new string('0', 32),
            ExpiresAt = Now.AddMinutes(30),
            UserId = account.Id
        };
    }

    // Enregistre directement un jeton déjà créé, puis oublie les objets gardés en mémoire.
    private static async Task SaveTokenDirectlyAsync(WalletFlowDatabaseContext databaseContext, EmailToken token)
    {
        databaseContext.EmailTokens.Add(token);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();
    }

    // Crée un événement du journal, sans l'enregistrer.
    private static SecurityEvent CreateSecurityEvent(User account, SecurityEventType eventType)
    {
        return new SecurityEvent
        {
            Type = eventType,
            Result = SecurityEventResult.Success,
            OccurredAt = Now,
            IpAddress = IPAddress.Parse("203.0.113.7"),
            UserId = account.Id
        };
    }

    // Prépare un nouveau mot de passe comme le fait le cas d'utilisation : mot de passe, compte débloqué, lien utilisé et événement.
    private static void PrepareNewPassword(
        WalletFlowDatabaseContext databaseContext,
        User account,
        EmailToken token,
        string newPasswordHash,
        SecurityEvent securityEvent)
    {
        account.ReplacePasswordHash(newPasswordHash);
        account.Unlock();
        token.MarkAsUsed(Now);
        new SecurityEventRepository(databaseContext).PrepareEvent(securityEvent);
    }

    // Prépare un déblocage comme le fait le cas d'utilisation : compte débloqué, lien utilisé et événement.
    private static void PrepareUnlock(
        WalletFlowDatabaseContext databaseContext,
        User account,
        EmailToken token,
        DateTime unlockDate,
        SecurityEvent securityEvent)
    {
        account.Unlock();
        token.MarkAsUsed(unlockDate);
        new SecurityEventRepository(databaseContext).PrepareEvent(securityEvent);
    }

    // Compte les événements du journal pour ce compte.
    private static async Task<int> CountSecurityEventsAsync(WalletFlowDatabaseContext databaseContext, Guid accountId)
    {
        return await databaseContext.SecurityEvents.CountAsync(row => row.UserId == accountId);
    }

    // Un lien ne sert jamais à un autre usage que le sien.
    [Fact]
    public async Task TokenIsFoundOnlyWithItsOwnType()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-lien-par-type@exemple.test");
        var savedToken = CreateEmailToken(savedAccount, EmailTokenType.AccountUnlock);
        await SaveTokenDirectlyAsync(databaseContext, savedToken);

        Assert.NotNull(await repository.FindTokenAsync(savedToken.TokenHash, EmailTokenType.AccountUnlock));
        Assert.Null(await repository.FindTokenAsync(savedToken.TokenHash, EmailTokenType.PasswordRecovery));
    }

    // SS §2 : le jeton est invalidé dès qu'un nouveau jeton du même type est demandé.
    [Fact]
    public async Task NewTokenReplacesOnlyTheTokenOfTheSameTypeAndTheSameAccount()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-lien-remplace@exemple.test");
        var otherAccount = await SaveConfirmedAccountAsync(databaseContext, "test-lien-autre-compte@exemple.test");
        var oldToken = CreateEmailToken(savedAccount, EmailTokenType.PasswordRecovery);
        var otherTypeToken = CreateEmailToken(savedAccount, EmailTokenType.AccountUnlock);
        var otherAccountToken = CreateEmailToken(otherAccount, EmailTokenType.PasswordRecovery);
        await SaveTokenDirectlyAsync(databaseContext, oldToken);
        await SaveTokenDirectlyAsync(databaseContext, otherTypeToken);
        await SaveTokenDirectlyAsync(databaseContext, otherAccountToken);
        var newToken = CreateEmailToken(savedAccount, EmailTokenType.PasswordRecovery);

        await repository.ReplaceTokenAsync(newToken);

        databaseContext.ChangeTracker.Clear();
        Assert.Null(await repository.FindTokenAsync(oldToken.TokenHash, EmailTokenType.PasswordRecovery));
        Assert.NotNull(await repository.FindTokenAsync(newToken.TokenHash, EmailTokenType.PasswordRecovery));
        Assert.NotNull(await repository.FindTokenAsync(otherTypeToken.TokenHash, EmailTokenType.AccountUnlock));
        Assert.NotNull(await repository.FindTokenAsync(otherAccountToken.TokenHash, EmailTokenType.PasswordRecovery));
    }

    // L'ancien lien vient de servir au même instant : le remplacement doit réussir quand même, sinon le nouvel e-mail ne partirait pas.
    [Fact]
    public async Task NewTokenReplacesAnOldTokenThatWasJustUsedByAnotherRequest()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-lien-utilise-et-remplace@exemple.test");
        var oldToken = CreateEmailToken(savedAccount, EmailTokenType.PasswordRecovery);
        oldToken.MarkAsUsed(Now);
        await SaveTokenDirectlyAsync(databaseContext, oldToken);
        var newToken = CreateEmailToken(savedAccount, EmailTokenType.PasswordRecovery);

        await repository.ReplaceTokenAsync(newToken);

        databaseContext.ChangeTracker.Clear();
        Assert.Null(await repository.FindTokenAsync(oldToken.TokenHash, EmailTokenType.PasswordRecovery));
        Assert.NotNull(await repository.FindTokenAsync(newToken.TokenHash, EmailTokenType.PasswordRecovery));
    }

    // Le remplacement d'un lien est une écriture immédiate : il n'emporte jamais un compte changé qui attend l'unité de travail.
    [Fact]
    public async Task TokenReplacementRefusesToCarryChangesWaitingForTheUnitOfWork()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-lien-changements-en-attente@exemple.test");
        var account = await repository.FindByIdAsync(savedAccount.Id);
        account!.CountLoginAttempt(Now);
        var newToken = CreateEmailToken(savedAccount, EmailTokenType.PasswordRecovery);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ReplaceTokenAsync(newToken));

        databaseContext.ChangeTracker.Clear();
        Assert.Null(await repository.FindTokenAsync(newToken.TokenHash, EmailTokenType.PasswordRecovery));
        var reloadedAccount = await repository.FindByIdAsync(savedAccount.Id);
        Assert.Equal(0, reloadedAccount!.FailedLoginAttemptCount);
    }

    // Ce test n'ouvre pas de transaction : il vérifie celle du dépôt. Il efface donc lui-même son compte à la fin.
    // Le nouveau jeton reprend l'empreinte d'un autre lien du compte : la base le refuse après l'effacement de l'ancien.
    [Fact]
    public async Task OldTokenIsKeptWhenTheNewTokenCannotBeSaved()
    {
        await using var databaseContext = CreateDatabaseContext();
        var repository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-lien-garde-si-echec@exemple.test");

        try
        {
            var oldToken = CreateEmailToken(savedAccount, EmailTokenType.PasswordRecovery);
            var otherTypeToken = CreateEmailToken(savedAccount, EmailTokenType.AccountUnlock);
            await SaveTokenDirectlyAsync(databaseContext, oldToken);
            await SaveTokenDirectlyAsync(databaseContext, otherTypeToken);
            var newToken = new EmailToken
            {
                Type = EmailTokenType.PasswordRecovery,
                TokenHash = otherTypeToken.TokenHash,
                ExpiresAt = Now.AddMinutes(30),
                UserId = savedAccount.Id
            };

            await Assert.ThrowsAsync<DbUpdateException>(() => repository.ReplaceTokenAsync(newToken));

            databaseContext.ChangeTracker.Clear();
            Assert.NotNull(await repository.FindTokenAsync(oldToken.TokenHash, EmailTokenType.PasswordRecovery));
        }
        finally
        {
            databaseContext.ChangeTracker.Clear();
            await databaseContext.Users
                .Where(savedUser => savedUser.Id == savedAccount.Id)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task NewPasswordIsReallySavedWithTheUsedTokenAndTheSecurityEvent()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-mot-de-passe-enregistre@exemple.test");
        var savedToken = CreateEmailToken(savedAccount, EmailTokenType.PasswordRecovery);
        await SaveTokenDirectlyAsync(databaseContext, savedToken);
        var account = await repository.FindByIdAsync(savedAccount.Id);
        var token = await repository.FindTokenAsync(savedToken.TokenHash, EmailTokenType.PasswordRecovery);
        PrepareNewPassword(
            databaseContext,
            account!,
            token!,
            "empreinte-du-nouveau-mot-de-passe",
            CreateSecurityEvent(savedAccount, SecurityEventType.PasswordChange));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.True(saved);
        databaseContext.ChangeTracker.Clear();
        var reloadedAccount = await repository.FindByIdAsync(savedAccount.Id);
        var reloadedToken = await repository.FindTokenAsync(savedToken.TokenHash, EmailTokenType.PasswordRecovery);
        Assert.Equal("empreinte-du-nouveau-mot-de-passe", reloadedAccount!.PasswordHash);
        Assert.Equal(Now, reloadedToken!.UsedAt);
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, savedAccount.Id));
    }

    // Tout ou rien : si le mot de passe vient de changer, ni le jeton ni le journal ne sont modifiés.
    [Fact]
    public async Task NothingIsSavedWhenAnotherRequestJustChangedThePassword()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-mot-de-passe-conflit@exemple.test");
        var savedToken = CreateEmailToken(savedAccount, EmailTokenType.PasswordRecovery);
        await SaveTokenDirectlyAsync(databaseContext, savedToken);
        var account = await repository.FindByIdAsync(savedAccount.Id);
        var token = await repository.FindTokenAsync(savedToken.TokenHash, EmailTokenType.PasswordRecovery);

        await WriteAsAnotherRequestAsync(databaseContext, () => databaseContext.Users
            .Where(row => row.Id == savedAccount.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                row => row.PasswordHash,
                "empreinte-d-une-autre-requete")));

        PrepareNewPassword(
            databaseContext,
            account!,
            token!,
            "empreinte-du-nouveau-mot-de-passe",
            CreateSecurityEvent(savedAccount, SecurityEventType.PasswordChange));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.False(saved);
        var reloadedAccount = await repository.FindByIdAsync(savedAccount.Id);
        var reloadedToken = await repository.FindTokenAsync(savedToken.TokenHash, EmailTokenType.PasswordRecovery);
        Assert.Equal("empreinte-d-une-autre-requete", reloadedAccount!.PasswordHash);
        Assert.Null(reloadedToken!.UsedAt);
        Assert.Equal(0, await CountSecurityEventsAsync(databaseContext, savedAccount.Id));
    }

    // Usage unique contrôlé en base : deux clics simultanés sur le même lien ne peuvent pas réussir tous les deux.
    [Fact]
    public async Task UnlockIsNotSavedWhenAnotherRequestJustUsedTheSameLink()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-deblocage-conflit@exemple.test");
        var savedToken = CreateEmailToken(savedAccount, EmailTokenType.AccountUnlock);
        await SaveTokenDirectlyAsync(databaseContext, savedToken);
        var account = await repository.FindByIdAsync(savedAccount.Id);
        var token = await repository.FindTokenAsync(savedToken.TokenHash, EmailTokenType.AccountUnlock);

        await WriteAsAnotherRequestAsync(databaseContext, () => databaseContext.EmailTokens
            .Where(row => row.Id == savedToken.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                row => row.UsedAt,
                Now)));

        PrepareUnlock(
            databaseContext,
            account!,
            token!,
            Now.AddMinutes(1),
            CreateSecurityEvent(savedAccount, SecurityEventType.Unlock));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.False(saved);
        Assert.Equal(0, await CountSecurityEventsAsync(databaseContext, savedAccount.Id));
    }

    // Un nouveau blocage a remplacé le lien : l'ancien lien ne doit plus rien débloquer.
    [Fact]
    public async Task UnlockIsNotSavedWhenTheLinkWasJustReplaced()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-deblocage-remplace@exemple.test");
        var savedToken = CreateEmailToken(savedAccount, EmailTokenType.AccountUnlock);
        await SaveTokenDirectlyAsync(databaseContext, savedToken);
        var account = await repository.FindByIdAsync(savedAccount.Id);
        var token = await repository.FindTokenAsync(savedToken.TokenHash, EmailTokenType.AccountUnlock);

        await databaseContext.EmailTokens
            .Where(row => row.Id == savedToken.Id)
            .ExecuteDeleteAsync();

        PrepareUnlock(
            databaseContext,
            account!,
            token!,
            Now,
            CreateSecurityEvent(savedAccount, SecurityEventType.Unlock));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.False(saved);
    }

    [Fact]
    public async Task UnlockIsReallySavedWithTheCountersTheUsedTokenAndTheSecurityEvent()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var repository = new UserAccountRepository(databaseContext);
        var savedAccount = await SaveConfirmedAccountAsync(databaseContext, "test-deblocage-enregistre@exemple.test");
        var savedToken = CreateEmailToken(savedAccount, EmailTokenType.AccountUnlock);
        await SaveTokenDirectlyAsync(databaseContext, savedToken);
        await databaseContext.Users
            .Where(row => row.Id == savedAccount.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.FailedLoginAttemptCount, (short)5)
                .SetProperty(row => row.LockoutCount, (short)2)
                .SetProperty(row => row.LockoutEndsAt, Now.AddMinutes(30)));
        var account = await repository.FindByIdAsync(savedAccount.Id);
        var token = await repository.FindTokenAsync(savedToken.TokenHash, EmailTokenType.AccountUnlock);

        PrepareUnlock(
            databaseContext,
            account!,
            token!,
            Now,
            CreateSecurityEvent(savedAccount, SecurityEventType.Unlock));
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.True(saved);
        databaseContext.ChangeTracker.Clear();
        var reloadedAccount = await repository.FindByIdAsync(savedAccount.Id);
        var reloadedToken = await repository.FindTokenAsync(savedToken.TokenHash, EmailTokenType.AccountUnlock);
        Assert.Equal(0, reloadedAccount!.FailedLoginAttemptCount);
        Assert.Null(reloadedAccount.LockoutEndsAt);
        Assert.Equal(2, reloadedAccount.LockoutCount);
        Assert.Equal(Now, reloadedToken!.UsedAt);
        Assert.Equal(1, await CountSecurityEventsAsync(databaseContext, savedAccount.Id));
    }
}
