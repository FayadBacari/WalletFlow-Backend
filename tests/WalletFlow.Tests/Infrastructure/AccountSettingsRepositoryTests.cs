using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie dans la vraie base PostgreSQL locale ce dont l'étape « compte et données personnelles » a besoin, avec l'utilisateur de l'API.
// Chaque test travaille dans une transaction annulée à la fin : la base reste vide.
public class AccountSettingsRepositoryTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    // Enregistre tout de suite un lien de ce type pour ce compte, puis oublie les objets gardés en mémoire.
    private static async Task<EmailToken> SaveLinkAsync(WalletFlowDatabaseContext databaseContext, User account, EmailTokenType linkType, string? newEmail)
    {
        var link = new EmailToken
        {
            Type = linkType,
            TokenHash = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + new string('0', 32),
            ExpiresAt = Now.AddMinutes(30),
            NewEmail = newEmail,
            UserId = account.Id
        };
        databaseContext.EmailTokens.Add(link);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        return link;
    }

    // Enregistre tout de suite une session de ce compte, puis oublie les objets gardés en mémoire.
    private static async Task<UserSession> SaveSessionAsync(WalletFlowDatabaseContext databaseContext, User account)
    {
        var session = CreateSession(account);
        databaseContext.UserSessions.Add(session);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        return session;
    }

    // Crée une session de ce compte, sans l'enregistrer.
    private static UserSession CreateSession(User account)
    {
        return new UserSession
        {
            Id = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + new string('0', 32),
            LastActivityAt = Now,
            UserId = account.Id
        };
    }

    // Donne les types des liens de ce compte, triés.
    private static async Task<List<EmailTokenType>> ReadLinkTypesAsync(WalletFlowDatabaseContext databaseContext, User account)
    {
        var linkTypes = await databaseContext.EmailTokens
            .Where(token => token.UserId == account.Id)
            .Select(token => token.Type)
            .ToListAsync();
        linkTypes.Sort();

        return linkTypes;
    }

    // SS §2 : confirmer une nouvelle adresse annule les liens de récupération et de déblocage partis vers l'ancienne.
    [Fact]
    public async Task RecoveryAndUnlockLinksOfTheAccountAreRemovedWithTheOtherChanges()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveConfirmedAccountAsync(databaseContext, "test-liens-nouvelle-adresse@exemple.test");
        var otherAccount = await SaveConfirmedAccountAsync(databaseContext, "test-liens-autre-compte@exemple.test");
        await SaveLinkAsync(databaseContext, account, EmailTokenType.PasswordRecovery, null);
        await SaveLinkAsync(databaseContext, account, EmailTokenType.AccountUnlock, null);
        await SaveLinkAsync(databaseContext, account, EmailTokenType.EmailChange, "nouvelle-adresse@exemple.test");
        await SaveLinkAsync(databaseContext, otherAccount, EmailTokenType.PasswordRecovery, null);

        await new UserAccountRepository(databaseContext).PrepareRecoveryAndUnlockLinksRemovalAsync(account.Id);
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.True(saved);
        databaseContext.ChangeTracker.Clear();
        Assert.Equal(new List<EmailTokenType> { EmailTokenType.EmailChange }, await ReadLinkTypesAsync(databaseContext, account));
        Assert.Equal(new List<EmailTokenType> { EmailTokenType.PasswordRecovery }, await ReadLinkTypesAsync(databaseContext, otherAccount));
    }

    // SS §3 : toutes les sessions du compte se ferment, sauf la nouvelle session préparée avec elles.
    [Fact]
    public async Task AllSessionsOfTheAccountAreRemovedExceptTheNewOnePreparedWithThem()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveConfirmedAccountAsync(databaseContext, "test-sessions-fermees@exemple.test");
        var otherAccount = await SaveConfirmedAccountAsync(databaseContext, "test-sessions-autre-compte@exemple.test");
        await SaveSessionAsync(databaseContext, account);
        await SaveSessionAsync(databaseContext, account);
        var otherAccountSession = await SaveSessionAsync(databaseContext, otherAccount);
        var sessionRepository = new UserSessionRepository(databaseContext);
        var newSession = CreateSession(account);

        await sessionRepository.PrepareAllSessionsRemovalAsync(account.Id);
        sessionRepository.PrepareSession(newSession);
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.True(saved);
        databaseContext.ChangeTracker.Clear();
        var remainingSessionIds = await databaseContext.UserSessions
            .Where(session => session.UserId == account.Id)
            .Select(session => session.Id)
            .ToListAsync();
        Assert.Equal(new List<string> { newSession.Id }, remainingSessionIds);
        Assert.NotNull(await sessionRepository.FindAsync(otherAccountSession.Id));
    }

    // Une déconnexion efface une session entre la lecture et l'enregistrement : c'est un conflit, et le passage recommence.
    [Fact]
    public async Task SessionClosedByAnotherRequestMakesTheRemovalConflict()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveConfirmedAccountAsync(databaseContext, "test-sessions-deconnexion@exemple.test");
        var session = await SaveSessionAsync(databaseContext, account);

        await new UserSessionRepository(databaseContext).PrepareAllSessionsRemovalAsync(account.Id);
        await WriteAsAnotherRequestAsync(databaseContext, () => databaseContext.UserSessions
            .Where(row => row.Id == session.Id)
            .ExecuteDeleteAsync());
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.False(saved);
    }

    // Deux comptes confirment la même adresse au même instant : la base refuse le second, et l'unité de travail le dit
    // comme un conflit, sans erreur. La transaction reste utilisable : le passage suivant peut tout relire.
    [Fact]
    public async Task AddressTakenByAnotherAccountIsAConflictNotAFailure()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        await SaveConfirmedAccountAsync(databaseContext, "test-adresse-gardee@exemple.test");
        var secondAccount = await SaveConfirmedAccountAsync(databaseContext, "test-adresse-voulue@exemple.test");
        var trackedSecondAccount = await new UserAccountRepository(databaseContext).FindByIdAsync(secondAccount.Id);

        trackedSecondAccount!.ReplaceEmail("test-adresse-gardee@exemple.test");
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.False(saved);
        Assert.Equal(1, await databaseContext.Users.CountAsync(user => user.Email == "test-adresse-gardee@exemple.test"));
        Assert.Equal(1, await databaseContext.Users.CountAsync(user => user.Email == "test-adresse-voulue@exemple.test"));
    }

    // Toute autre erreur de la base reste une panne : elle n'est jamais prise pour un conflit.
    [Fact]
    public async Task OtherDatabaseErrorIsStillAFailure()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveConfirmedAccountAsync(databaseContext, "test-autre-erreur@exemple.test");
        var sessionWithTooLongIdentifier = new UserSession
        {
            Id = new string('a', 65),
            LastActivityAt = Now,
            UserId = account.Id
        };

        new UserSessionRepository(databaseContext).PrepareSession(sessionWithTooLongIdentifier);

        await Assert.ThrowsAsync<DbUpdateException>(() => TrySaveChangesAsync(databaseContext));
    }

    // Le rôle de l'API a le droit d'écrire l'adresse, le nom et le prénom (MPD, droits par colonne).
    [Fact]
    public async Task NewEmailNameAndFirstNameAreSavedByTheApplicationRole()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveConfirmedAccountAsync(databaseContext, "test-ancienne-adresse@exemple.test");
        var repository = new UserAccountRepository(databaseContext);
        var trackedAccount = await repository.FindByIdAsync(account.Id);

        trackedAccount!.ReplaceEmail("test-nouvelle-adresse@exemple.test");
        trackedAccount.ReplaceName("Dupont", "Élise");
        var saved = await TrySaveChangesAsync(databaseContext);

        Assert.True(saved);
        databaseContext.ChangeTracker.Clear();
        var reloadedAccount = await repository.FindByIdAsync(account.Id);
        Assert.Equal("test-nouvelle-adresse@exemple.test", reloadedAccount!.Email);
        Assert.Equal("Dupont", reloadedAccount.LastName);
        Assert.Equal("Élise", reloadedAccount.FirstName);
    }

    // Le lien d'un changement d'adresse garde la nouvelle adresse ; la base refuse une nouvelle adresse sur un autre type de lien (MPD).
    [Fact]
    public async Task OnlyAnEmailChangeLinkCarriesANewAddress()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveConfirmedAccountAsync(databaseContext, "test-lien-nouvelle-adresse@exemple.test");
        var emailChangeLink = await SaveLinkAsync(databaseContext, account, EmailTokenType.EmailChange, "test-destination@exemple.test");

        var reloadedLink = await new UserAccountRepository(databaseContext).FindTokenAsync(emailChangeLink.TokenHash, EmailTokenType.EmailChange);

        Assert.Equal("test-destination@exemple.test", reloadedLink!.NewEmail);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            SaveLinkAsync(databaseContext, account, EmailTokenType.PasswordRecovery, "test-destination@exemple.test"));
    }
}
