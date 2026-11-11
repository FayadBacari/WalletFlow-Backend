using System.Net;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le choix d'un nouveau mot de passe avec le lien reçu (GR Auth, SS §2, §3 et §11), avec de faux outils.
public class PasswordResetServiceTests
{
    private const string NewPassword = "Nouveau-Mot-De-Passe-2";
    private const string LinkToken = FakeSecureTokenGenerator.FixedToken;
    private const string OldHash = "ancienne-empreinte-du-compte";

    private static readonly IPAddress IpAddress = IPAddress.Parse("203.0.113.7");

    private readonly FakeUserAccountRepository accountRepository = new FakeUserAccountRepository();
    private readonly FakeUserSessionRepository sessionRepository = new FakeUserSessionRepository();
    private readonly FakeSecurityEventRepository securityEventRepository = new FakeSecurityEventRepository();
    private readonly FakePasswordHasher passwordHasher = new FakePasswordHasher();
    private readonly FakeTimeProvider clock = new FakeTimeProvider();

    // Crée le service à tester, avec ses faux outils.
    private PasswordResetService CreatePasswordResetService()
    {
        return new PasswordResetService(
            accountRepository,
            sessionRepository,
            new DatabaseChangesSavingService(new FakeDatabaseUnitOfWork(accountRepository, sessionRepository, securityEventRepository)),
            new SecurityJournalService(securityEventRepository, clock),
            passwordHasher,
            new FakeSecureTokenGenerator(),
            clock);
    }

    // Ajoute à la fausse base un compte confirmé, avec son lien de récupération valable 30 minutes.
    private User AddAccountWithRecoveryLink(
        bool isEmailConfirmed = true,
        EmailTokenType linkType = EmailTokenType.PasswordRecovery,
        short failedAttemptCount = 0,
        short lockoutCount = 0,
        DateTime? lockoutEndsAt = null)
    {
        var account = new User
        {
            Email = "utilisateur@exemple.test",
            PasswordHash = OldHash,
            IsEmailConfirmed = isEmailConfirmed,
            FailedLoginAttemptCount = failedAttemptCount,
            LockoutCount = lockoutCount,
            LockoutEndsAt = lockoutEndsAt
        };
        accountRepository.SavedUsers.Add(account);
        accountRepository.SavedEmailTokens.Add(new EmailToken
        {
            Type = linkType,
            TokenHash = FakeSecureTokenGenerator.FixedHash,
            ExpiresAt = clock.GetUtcNow().UtcDateTime.AddMinutes(30),
            UserId = account.Id
        });

        return account;
    }

    // Ajoute à la fausse base une session de ce compte.
    private void AddSessionForAccount(Guid accountId)
    {
        sessionRepository.SavedSessions.Add(new UserSession
        {
            Id = Guid.NewGuid().ToString(),
            LastActivityAt = clock.GetUtcNow().UtcDateTime,
            UserId = accountId
        });
    }

    // Envoie un nouveau mot de passe avec le lien de ces tests.
    private async Task<PasswordResetResult> ResetPasswordAsync(string newPassword = NewPassword)
    {
        return await CreatePasswordResetService().ResetAsync(LinkToken, newPassword, IpAddress);
    }

    // Vérifie que le lien a été refusé, et que rien n'a changé.
    private void AssertLinkRefusedAndNothingChanged(PasswordResetResult result, User account)
    {
        Assert.Equal(PasswordResetResult.LinkNoLongerValid, result);
        Assert.Equal(OldHash, account.PasswordHash);
        Assert.Empty(securityEventRepository.SavedSecurityEvents);
    }

    // --- Changement réussi

    [Fact]
    public async Task RightLinkChangesThePasswordAndUsesTheLink()
    {
        var account = AddAccountWithRecoveryLink();

        var result = await ResetPasswordAsync();

        Assert.Equal(PasswordResetResult.PasswordChanged, result);
        Assert.Equal(FakePasswordHasher.HashPrefix + NewPassword, account.PasswordHash);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, accountRepository.SavedEmailTokens[0].UsedAt);
    }

    // GR Auth : une récupération réussie débloque aussi le compte, sans effacer le nombre de blocages.
    [Fact]
    public async Task SuccessfulResetUnlocksTheAccountAndKeepsTheNumberOfLockouts()
    {
        var account = AddAccountWithRecoveryLink(
            failedAttemptCount: 5,
            lockoutCount: 3,
            lockoutEndsAt: clock.GetUtcNow().UtcDateTime.AddHours(1));

        await ResetPasswordAsync();

        Assert.Equal(0, account.FailedLoginAttemptCount);
        Assert.Null(account.LockoutEndsAt);
        Assert.Equal(3, account.LockoutCount);
    }

    // SS §3 : après une récupération, toutes les sessions du compte sont fermées, et seulement les siennes.
    [Fact]
    public async Task SuccessfulResetClosesEverySessionOfTheAccountAndOnlyThose()
    {
        var account = AddAccountWithRecoveryLink();
        var otherAccountId = Guid.NewGuid();
        AddSessionForAccount(account.Id);
        AddSessionForAccount(account.Id);
        AddSessionForAccount(otherAccountId);

        await ResetPasswordAsync();

        Assert.Equal(otherAccountId, Assert.Single(sessionRepository.SavedSessions).UserId);
    }

    // GR Auth : la récupération ne connecte personne. L'utilisateur se connecte avec son nouveau mot de passe.
    [Fact]
    public async Task SuccessfulResetOpensNoSessionForTheVisitor()
    {
        AddAccountWithRecoveryLink();

        await ResetPasswordAsync();

        Assert.Empty(sessionRepository.SavedSessions);
    }

    [Fact]
    public async Task SuccessfulResetIsWrittenInTheJournalWithItsUserAndIpAddress()
    {
        var account = AddAccountWithRecoveryLink();

        await ResetPasswordAsync();

        var securityEvent = Assert.Single(securityEventRepository.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.PasswordChange, securityEvent.Type);
        Assert.Equal(SecurityEventResult.Success, securityEvent.Result);
        Assert.Equal(account.Id, securityEvent.UserId);
        Assert.Equal(IpAddress, securityEvent.IpAddress);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, securityEvent.OccurredAt);
    }

    // Aucune règle n'interdit de garder le même mot de passe : le lien a prouvé qu'il possède la boîte mail.
    [Fact]
    public async Task SamePasswordAsTheOldOneIsAccepted()
    {
        var account = AddAccountWithRecoveryLink();
        account.ReplacePasswordHash(FakePasswordHasher.HashPrefix + NewPassword);

        var result = await ResetPasswordAsync();

        Assert.Equal(PasswordResetResult.PasswordChanged, result);
    }

    // --- Règles du mot de passe

    [Theory]
    [InlineData("trop-court")]
    [InlineData("")]
    public async Task PasswordBreakingTheRulesIsRefusedAndKeepsTheLinkUsable(string newPassword)
    {
        var account = AddAccountWithRecoveryLink();
        AddSessionForAccount(account.Id);

        var result = await ResetPasswordAsync(newPassword);

        Assert.Equal(PasswordResetResult.PasswordBreaksRules, result);
        Assert.Equal(OldHash, account.PasswordHash);
        Assert.Null(accountRepository.SavedEmailTokens[0].UsedAt);
        Assert.Single(sessionRepository.SavedSessions);
        Assert.Equal(0, passwordHasher.HashedPasswordCount);

        var laterResult = await ResetPasswordAsync();
        Assert.Equal(PasswordResetResult.PasswordChanged, laterResult);
    }

    // SS §2 : le mot de passe est normalisé avant les règles et le hachage.
    [Fact]
    public async Task PasswordTypedWithDecomposedAccentIsHashedInItsNormalizedForm()
    {
        var account = AddAccountWithRecoveryLink();

        await ResetPasswordAsync("Mot-De-Passé-1");

        Assert.Equal(FakePasswordHasher.HashPrefix + "Mot-De-Passé-1", account.PasswordHash);
    }

    [Fact]
    public async Task PasswordLengthIsCountedAfterTheNormalization()
    {
        AddAccountWithRecoveryLink();

        var result = await ResetPasswordAsync("Abcdefgh-1é");

        Assert.Equal(PasswordResetResult.PasswordBreaksRules, result);
    }

    [Theory]
    [InlineData(0xFFFE)]
    [InlineData(0x200B)] // espace invisible
    public async Task PasswordWithForbiddenCharacterIsRefusedWithoutAnyError(int forbiddenCharacter)
    {
        AddAccountWithRecoveryLink();

        var result = await ResetPasswordAsync(NewPassword + (char)forbiddenCharacter);

        Assert.Equal(PasswordResetResult.PasswordBreaksRules, result);
    }

    // --- Liens refusés

    [Fact]
    public async Task UnknownLinkIsRefusedWithoutHashingThePassword()
    {
        var account = AddAccountWithRecoveryLink();

        var result = await CreatePasswordResetService().ResetAsync("jeton-invente", NewPassword, IpAddress);

        AssertLinkRefusedAndNothingChanged(result, account);
        Assert.Equal(0, passwordHasher.HashedPasswordCount);
    }

    [Fact]
    public async Task LinkAtItsExactExpirationDateIsRefused()
    {
        var account = AddAccountWithRecoveryLink();
        clock.Advance(TimeSpan.FromMinutes(30));

        var result = await ResetPasswordAsync();

        AssertLinkRefusedAndNothingChanged(result, account);
    }

    [Fact]
    public async Task LinkJustBeforeItsExpirationDateIsAccepted()
    {
        AddAccountWithRecoveryLink();
        clock.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(1));

        var result = await ResetPasswordAsync();

        Assert.Equal(PasswordResetResult.PasswordChanged, result);
    }

    [Fact]
    public async Task LinkAlreadyUsedOnceIsRefusedAgain()
    {
        AddAccountWithRecoveryLink();
        await ResetPasswordAsync();
        var securityEvents = securityEventRepository.SavedSecurityEvents.Count;

        var result = await ResetPasswordAsync("Encore-Un-Mot-De-Passe-3");

        Assert.Equal(PasswordResetResult.LinkNoLongerValid, result);
        Assert.Equal(securityEvents, securityEventRepository.SavedSecurityEvents.Count);
    }

    // Un lien de déblocage ne doit jamais permettre de changer un mot de passe.
    [Theory]
    [InlineData(EmailTokenType.AccountUnlock)]
    [InlineData(EmailTokenType.SignupConfirmation)]
    [InlineData(EmailTokenType.EmailChange)]
    public async Task LinkOfAnotherTypeIsRefused(EmailTokenType linkType)
    {
        var account = AddAccountWithRecoveryLink(linkType: linkType);

        var result = await ResetPasswordAsync();

        AssertLinkRefusedAndNothingChanged(result, account);
    }

    [Fact]
    public async Task LinkOfAnAccountNotYetConfirmedIsRefused()
    {
        var account = AddAccountWithRecoveryLink(isEmailConfirmed: false);

        var result = await ResetPasswordAsync();

        AssertLinkRefusedAndNothingChanged(result, account);
    }

    [Fact]
    public async Task LinkOfAnAccountThatNoLongerExistsIsRefused()
    {
        var account = AddAccountWithRecoveryLink();
        accountRepository.SavedUsers.Clear();

        var result = await ResetPasswordAsync();

        AssertLinkRefusedAndNothingChanged(result, account);
    }

    // --- Requêtes simultanées et pannes

    // Un essai de connexion a changé le compte au même instant : le changement de mot de passe doit quand même réussir.
    [Fact]
    public async Task ConflictTriesAgainAndStillChangesThePassword()
    {
        var account = AddAccountWithRecoveryLink();
        accountRepository.LinkUseConflicts = 3;

        var result = await ResetPasswordAsync();

        Assert.Equal(PasswordResetResult.PasswordChanged, result);
        Assert.Equal(FakePasswordHasher.HashPrefix + NewPassword, account.PasswordHash);
        Assert.Single(securityEventRepository.SavedSecurityEvents);
        Assert.Equal(1, passwordHasher.HashedPasswordCount);
    }

    // Double clic : le lien vient de servir à une autre requête, et les sessions ne sont pas touchées une seconde fois.
    [Fact]
    public async Task LinkUsedByAnotherRequestJustBeforeTheSaveIsRefused()
    {
        var account = AddAccountWithRecoveryLink();
        AddSessionForAccount(account.Id);
        accountRepository.UseLinkBeforeSave = true;

        var result = await ResetPasswordAsync();

        AssertLinkRefusedAndNothingChanged(result, account);
        Assert.Single(sessionRepository.SavedSessions);
    }

    [Fact]
    public async Task TooManyConflictsRefuseTheLinkAfterTenTries()
    {
        var account = AddAccountWithRecoveryLink();
        accountRepository.LinkUseConflicts = 1000;

        var result = await ResetPasswordAsync();

        AssertLinkRefusedAndNothingChanged(result, account);
        Assert.Equal(990, accountRepository.LinkUseConflicts);
    }

    // Le seul moment non atomique : la panne n'est pas cachée, et la personne peut refaire une récupération.
    [Fact]
    public async Task DatabaseFailureWhileClosingTheSessionsIsNotHidden()
    {
        AddAccountWithRecoveryLink();
        sessionRepository.FailsWhenClosingAllSessions = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ResetPasswordAsync());
    }

    [Fact]
    public async Task DatabaseFailureWhileReadingTheLinkIsNotHiddenAndChangesNothing()
    {
        var account = AddAccountWithRecoveryLink();
        accountRepository.FailsWhenReadingLinks = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ResetPasswordAsync());

        Assert.Equal(OldHash, account.PasswordHash);
    }
}
