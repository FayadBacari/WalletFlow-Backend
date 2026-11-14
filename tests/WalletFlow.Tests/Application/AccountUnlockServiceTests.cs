using System.Net;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le déblocage avec le lien reçu (GR Auth, SS §2 et §11), avec de faux outils.
public class AccountUnlockServiceTests
{
    private const string LinkToken = FakeSecureTokenGenerator.FixedToken;

    private static readonly IPAddress IpAddress = IPAddress.Parse("203.0.113.7");

    private readonly FakeUserAccountRepository accountRepository = new FakeUserAccountRepository();
    private readonly FakeSecurityEventRepository securityEventRepository = new FakeSecurityEventRepository();
    private readonly FakeTimeProvider clock = new FakeTimeProvider();

    // Crée le service à tester, avec ses faux outils.
    private AccountUnlockService CreateUnlockService()
    {
        return new AccountUnlockService(
            accountRepository,
            new DatabaseChangesSavingService(new FakeDatabaseUnitOfWork(accountRepository, securityEventRepository)),
            new SecurityJournalService(securityEventRepository, clock),
            new FakeSecureTokenGenerator(),
            clock);
    }

    // Ajoute à la fausse base un compte bloqué, avec son lien de déblocage valable 1 heure.
    private User AddLockedAccountWithUnlockLink(EmailTokenType linkType = EmailTokenType.AccountUnlock, DateTime? lockoutEndsAt = null)
    {
        var account = new User
        {
            Email = "utilisateur@exemple.test",
            IsEmailConfirmed = true,
            FailedLoginAttemptCount = 5,
            LockoutCount = 2,
            LockoutEndsAt = lockoutEndsAt ?? clock.GetUtcNow().UtcDateTime.AddMinutes(30)
        };
        accountRepository.SavedUsers.Add(account);
        accountRepository.SavedEmailTokens.Add(new EmailToken
        {
            Type = linkType,
            TokenHash = FakeSecureTokenGenerator.FixedHash,
            ExpiresAt = clock.GetUtcNow().UtcDateTime.AddHours(1),
            UserId = account.Id
        });

        return account;
    }

    // Clique sur le lien de ces tests.
    private async Task<AccountUnlockResult> UnlockAccountAsync()
    {
        return await CreateUnlockService().UnlockAsync(LinkToken, IpAddress);
    }

    // Vérifie que le lien a été refusé, et que le compte est resté bloqué, sans trace au journal.
    private void AssertLinkRefusedAndAccountLocked(AccountUnlockResult result, User account)
    {
        Assert.IsType<AccountUnlockResult.LinkNoLongerValid>(result);
        Assert.NotNull(account.LockoutEndsAt);
        Assert.Equal(5, account.FailedLoginAttemptCount);
        Assert.Empty(securityEventRepository.SavedSecurityEvents);
    }

    // GR Auth : le lien remet le compteur de tentatives à zéro. Le nombre de blocages ne change pas.
    [Fact]
    public async Task RightLinkEndsTheLockoutAndKeepsTheNumberOfLockouts()
    {
        var account = AddLockedAccountWithUnlockLink();

        var result = await UnlockAccountAsync();

        Assert.IsType<AccountUnlockResult.AccountUnlocked>(result);
        Assert.Equal(0, account.FailedLoginAttemptCount);
        Assert.Null(account.LockoutEndsAt);
        Assert.Equal(2, account.LockoutCount);
    }

    [Fact]
    public async Task RightLinkIsUsedOnlyOnceAtTheCurrentDate()
    {
        AddLockedAccountWithUnlockLink();

        await UnlockAccountAsync();

        Assert.Equal(clock.GetUtcNow().UtcDateTime, accountRepository.SavedEmailTokens[0].UsedAt);

        var result = await UnlockAccountAsync();
        Assert.IsType<AccountUnlockResult.LinkNoLongerValid>(result);
        Assert.Single(securityEventRepository.SavedSecurityEvents);
    }

    [Fact]
    public async Task SuccessfulUnlockIsWrittenInTheJournalWithItsUserAndIpAddress()
    {
        var account = AddLockedAccountWithUnlockLink();

        await UnlockAccountAsync();

        var securityEvent = Assert.Single(securityEventRepository.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.Unlock, securityEvent.Type);
        Assert.Equal(SecurityEventResult.Success, securityEvent.Result);
        Assert.Equal(account.Id, securityEvent.UserId);
        Assert.Equal(IpAddress, securityEvent.IpAddress);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, securityEvent.OccurredAt);
    }

    // Le blocage de 15 minutes peut finir avant le clic : le lien reste valable 1 heure, et répond la même chose.
    [Fact]
    public async Task LinkClickedAfterTheEndOfTheLockoutStillAnswersTheSame()
    {
        var account = AddLockedAccountWithUnlockLink();
        clock.Advance(TimeSpan.FromMinutes(45));

        var result = await UnlockAccountAsync();

        Assert.IsType<AccountUnlockResult.AccountUnlocked>(result);
        Assert.Equal(0, account.FailedLoginAttemptCount);
    }

    [Fact]
    public async Task UnknownLinkOfTheAccountIsRefused()
    {
        var account = AddLockedAccountWithUnlockLink();

        var result = await CreateUnlockService().UnlockAsync("jeton-invente", IpAddress);

        AssertLinkRefusedAndAccountLocked(result, account);
    }

    [Fact]
    public async Task LinkAtItsExactExpirationDateIsRefused()
    {
        var account = AddLockedAccountWithUnlockLink(lockoutEndsAt: clock.GetUtcNow().UtcDateTime.AddHours(4));
        clock.Advance(TimeSpan.FromHours(1));

        var result = await UnlockAccountAsync();

        AssertLinkRefusedAndAccountLocked(result, account);
    }

    // Un lien de récupération ne doit jamais servir à débloquer sans changer de mot de passe.
    [Theory]
    [InlineData(EmailTokenType.PasswordRecovery)]
    [InlineData(EmailTokenType.SignupConfirmation)]
    [InlineData(EmailTokenType.EmailChange)]
    public async Task LinkOfAnotherTypeIsRefused(EmailTokenType linkType)
    {
        var account = AddLockedAccountWithUnlockLink(linkType);

        var result = await UnlockAccountAsync();

        AssertLinkRefusedAndAccountLocked(result, account);
    }

    [Fact]
    public async Task LinkOfAnAccountThatNoLongerExistsIsRefused()
    {
        var account = AddLockedAccountWithUnlockLink();
        accountRepository.SavedUsers.Clear();

        var result = await UnlockAccountAsync();

        AssertLinkRefusedAndAccountLocked(result, account);
    }

    [Fact]
    public async Task ConflictTriesAgainAndStillUnlocksTheAccount()
    {
        var account = AddLockedAccountWithUnlockLink();
        accountRepository.LinkUseConflicts = 3;

        var result = await UnlockAccountAsync();

        Assert.IsType<AccountUnlockResult.AccountUnlocked>(result);
        Assert.Null(account.LockoutEndsAt);
        Assert.Single(securityEventRepository.SavedSecurityEvents);
    }

    [Fact]
    public async Task LinkUsedByAnotherRequestJustBeforeTheSaveIsRefused()
    {
        AddLockedAccountWithUnlockLink();
        accountRepository.UseLinkBeforeSave = true;

        var result = await UnlockAccountAsync();

        Assert.IsType<AccountUnlockResult.LinkNoLongerValid>(result);
        Assert.Empty(securityEventRepository.SavedSecurityEvents);
    }

    [Fact]
    public async Task TooManyConflictsRefuseTheLinkAfterTenTries()
    {
        AddLockedAccountWithUnlockLink();
        accountRepository.LinkUseConflicts = 1000;

        var result = await UnlockAccountAsync();

        Assert.IsType<AccountUnlockResult.LinkNoLongerValid>(result);
        Assert.Equal(990, accountRepository.LinkUseConflicts);
    }

    [Fact]
    public async Task DatabaseFailureWhileReadingTheLinkIsNotHidden()
    {
        AddLockedAccountWithUnlockLink();
        accountRepository.FailsWhenReadingLinks = true;

        await Assert.ThrowsAsync<InvalidOperationException>(UnlockAccountAsync);
    }
}
