using System.Net;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le déblocage avec le lien reçu (GR Auth, SS §2 et §11), avec de faux outils.
public class AccountUnlockServiceTests
{
    private const string TokenOfTheUnlockLinkInTheseTests = FakeSecureTokenGenerator.TokenAlwaysReturnedByThisFakeGenerator;

    private static readonly IPAddress IpAddressOfTheVisitorInTheseTests = IPAddress.Parse("203.0.113.7");

    private readonly FakeUserAccountRepository repositoryThatKeepsAccountsInMemory = new FakeUserAccountRepository();
    private readonly FakeTimeProviderForTests clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();

    // Crée le service à tester, avec ses faux outils.
    private AccountUnlockService CreateAccountUnlockServiceWithFakeTools()
    {
        return new AccountUnlockService(repositoryThatKeepsAccountsInMemory, new FakeSecureTokenGenerator(), clockThatOnlyMovesWhenTestsAskIt);
    }

    // Ajoute à la fausse base un compte bloqué, avec son lien de déblocage valable 1 heure.
    private User AddLockedAccountWithItsUnlockLinkToTheFakeDatabase(EmailTokenType typeOfTheLinkOfTheAccount = EmailTokenType.AccountUnlock)
    {
        var accountAddedToTheFakeDatabase = new User
        {
            Email = "utilisateur@exemple.test",
            IsEmailConfirmed = true,
            FailedLoginAttemptCount = 5,
            LockoutCount = 2,
            LockoutEndsAt = clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddMinutes(30)
        };
        repositoryThatKeepsAccountsInMemory.SavedUsers.Add(accountAddedToTheFakeDatabase);
        repositoryThatKeepsAccountsInMemory.SavedEmailTokens.Add(new EmailToken
        {
            Type = typeOfTheLinkOfTheAccount,
            TokenHash = FakeSecureTokenGenerator.HashAlwaysReturnedByThisFakeGenerator,
            ExpiresAt = clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddHours(1),
            UserId = accountAddedToTheFakeDatabase.Id
        });

        return accountAddedToTheFakeDatabase;
    }

    // Clique sur le lien de ces tests.
    private async Task<AccountUnlockResult> UnlockAccountWithTheLinkOfTheseTestsAsync()
    {
        return await CreateAccountUnlockServiceWithFakeTools().UnlockAccountWithLinkTokenAsync(TokenOfTheUnlockLinkInTheseTests, IpAddressOfTheVisitorInTheseTests);
    }

    // Vérifie que le lien a été refusé, et que le compte est resté bloqué, sans trace au journal.
    private void CheckThatLinkWasRefusedAndTheAccountStaysLocked(AccountUnlockResult resultOfTheAccountUnlockToCheck, User accountThatMustStayLockedInThisTest)
    {
        Assert.False(resultOfTheAccountUnlockToCheck.IsUnlocked);
        Assert.Equal(AccountUnlockService.ErrorMessageForUnlockLinkNoLongerValid, resultOfTheAccountUnlockToCheck.LinkErrorMessage);
        Assert.NotNull(accountThatMustStayLockedInThisTest.LockoutEndsAt);
        Assert.Equal(5, accountThatMustStayLockedInThisTest.FailedLoginAttemptCount);
        Assert.Empty(repositoryThatKeepsAccountsInMemory.SecurityEventsSavedWithTheLinks);
    }

    // GR Auth : le lien remet le compteur de tentatives à zéro. Le nombre de blocages ne change pas.
    [Fact]
    public async Task RightLinkEndsTheLockoutAndKeepsTheNumberOfLockouts()
    {
        var accountBeingTestedInThisUnlock = AddLockedAccountWithItsUnlockLinkToTheFakeDatabase();

        var resultOfTheAccountUnlockRequest = await UnlockAccountWithTheLinkOfTheseTestsAsync();

        Assert.True(resultOfTheAccountUnlockRequest.IsUnlocked);
        Assert.Equal(0, accountBeingTestedInThisUnlock.FailedLoginAttemptCount);
        Assert.Null(accountBeingTestedInThisUnlock.LockoutEndsAt);
        Assert.Equal(2, accountBeingTestedInThisUnlock.LockoutCount);
    }

    [Fact]
    public async Task RightLinkIsUsedOnlyOnceAtTheCurrentDate()
    {
        AddLockedAccountWithItsUnlockLinkToTheFakeDatabase();

        await UnlockAccountWithTheLinkOfTheseTestsAsync();

        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime, repositoryThatKeepsAccountsInMemory.SavedEmailTokens[0].UsedAt);

        var resultOfTheSecondClickOnTheLink = await UnlockAccountWithTheLinkOfTheseTestsAsync();
        Assert.Equal(AccountUnlockService.ErrorMessageForUnlockLinkNoLongerValid, resultOfTheSecondClickOnTheLink.LinkErrorMessage);
        Assert.Single(repositoryThatKeepsAccountsInMemory.SecurityEventsSavedWithTheLinks);
    }

    [Fact]
    public async Task SuccessfulUnlockIsWrittenInTheJournalWithItsUserAndIpAddress()
    {
        var accountBeingTestedInThisUnlock = AddLockedAccountWithItsUnlockLinkToTheFakeDatabase();

        await UnlockAccountWithTheLinkOfTheseTestsAsync();

        var securityEventOfTheAccountUnlock = Assert.Single(repositoryThatKeepsAccountsInMemory.SecurityEventsSavedWithTheLinks);
        Assert.Equal(SecurityEventType.Unlock, securityEventOfTheAccountUnlock.Type);
        Assert.Equal(SecurityEventResult.Success, securityEventOfTheAccountUnlock.Result);
        Assert.Equal(accountBeingTestedInThisUnlock.Id, securityEventOfTheAccountUnlock.UserId);
        Assert.Equal(IpAddressOfTheVisitorInTheseTests, securityEventOfTheAccountUnlock.IpAddress);
        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime, securityEventOfTheAccountUnlock.OccurredAt);
    }

    // Le blocage de 15 minutes peut finir avant le clic : le lien reste valable 1 heure, et répond la même chose.
    [Fact]
    public async Task LinkClickedAfterTheEndOfTheLockoutStillAnswersTheSame()
    {
        var accountBeingTestedInThisUnlock = AddLockedAccountWithItsUnlockLinkToTheFakeDatabase();
        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(45));

        var resultOfTheAccountUnlockRequest = await UnlockAccountWithTheLinkOfTheseTestsAsync();

        Assert.True(resultOfTheAccountUnlockRequest.IsUnlocked);
        Assert.Equal(0, accountBeingTestedInThisUnlock.FailedLoginAttemptCount);
    }

    [Fact]
    public async Task UnknownLinkOfTheAccountIsRefused()
    {
        var accountBeingTestedInThisUnlock = AddLockedAccountWithItsUnlockLinkToTheFakeDatabase();

        var resultOfTheAccountUnlockRequest = await CreateAccountUnlockServiceWithFakeTools().UnlockAccountWithLinkTokenAsync("jeton-invente", IpAddressOfTheVisitorInTheseTests);

        CheckThatLinkWasRefusedAndTheAccountStaysLocked(resultOfTheAccountUnlockRequest, accountBeingTestedInThisUnlock);
    }

    [Fact]
    public async Task LinkAtItsExactExpirationDateIsRefused()
    {
        var accountBeingTestedInThisUnlock = AddLockedAccountWithItsUnlockLinkToTheFakeDatabase();
        accountBeingTestedInThisUnlock.LockoutEndsAt = clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddHours(4);
        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromHours(1));

        var resultOfTheAccountUnlockRequest = await UnlockAccountWithTheLinkOfTheseTestsAsync();

        CheckThatLinkWasRefusedAndTheAccountStaysLocked(resultOfTheAccountUnlockRequest, accountBeingTestedInThisUnlock);
    }

    // Un lien de récupération ne doit jamais servir à débloquer sans changer de mot de passe.
    [Theory]
    [InlineData(EmailTokenType.PasswordRecovery)]
    [InlineData(EmailTokenType.SignupConfirmation)]
    [InlineData(EmailTokenType.EmailChange)]
    public async Task LinkOfAnotherTypeIsRefused(EmailTokenType typeOfTheLinkOfTheAccountInThisTest)
    {
        var accountBeingTestedInThisUnlock = AddLockedAccountWithItsUnlockLinkToTheFakeDatabase(typeOfTheLinkOfTheAccountInThisTest);

        var resultOfTheAccountUnlockRequest = await UnlockAccountWithTheLinkOfTheseTestsAsync();

        CheckThatLinkWasRefusedAndTheAccountStaysLocked(resultOfTheAccountUnlockRequest, accountBeingTestedInThisUnlock);
    }

    [Fact]
    public async Task LinkOfAnAccountThatNoLongerExistsIsRefused()
    {
        var accountBeingTestedInThisUnlock = AddLockedAccountWithItsUnlockLinkToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.SavedUsers.Clear();

        var resultOfTheAccountUnlockRequest = await UnlockAccountWithTheLinkOfTheseTestsAsync();

        CheckThatLinkWasRefusedAndTheAccountStaysLocked(resultOfTheAccountUnlockRequest, accountBeingTestedInThisUnlock);
    }

    [Fact]
    public async Task ConflictTriesAgainAndStillUnlocksTheAccount()
    {
        var accountBeingTestedInThisUnlock = AddLockedAccountWithItsUnlockLinkToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.NumberOfConflictsBeforeTheLinkIsUsed = 3;

        var resultOfTheAccountUnlockRequest = await UnlockAccountWithTheLinkOfTheseTestsAsync();

        Assert.True(resultOfTheAccountUnlockRequest.IsUnlocked);
        Assert.Null(accountBeingTestedInThisUnlock.LockoutEndsAt);
        Assert.Single(repositoryThatKeepsAccountsInMemory.SecurityEventsSavedWithTheLinks);
    }

    [Fact]
    public async Task LinkUsedByAnotherRequestJustBeforeTheSaveIsRefused()
    {
        AddLockedAccountWithItsUnlockLinkToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.LinkIsUsedByAnotherRequestJustBeforeTheSave = true;

        var resultOfTheAccountUnlockRequest = await UnlockAccountWithTheLinkOfTheseTestsAsync();

        Assert.Equal(AccountUnlockService.ErrorMessageForUnlockLinkNoLongerValid, resultOfTheAccountUnlockRequest.LinkErrorMessage);
        Assert.Empty(repositoryThatKeepsAccountsInMemory.SecurityEventsSavedWithTheLinks);
    }

    [Fact]
    public async Task TooManyConflictsRefuseTheLinkAfterTenTries()
    {
        AddLockedAccountWithItsUnlockLinkToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.NumberOfConflictsBeforeTheLinkIsUsed = 1000;

        var resultOfTheAccountUnlockRequest = await UnlockAccountWithTheLinkOfTheseTestsAsync();

        Assert.Equal(AccountUnlockService.ErrorMessageForUnlockLinkNoLongerValid, resultOfTheAccountUnlockRequest.LinkErrorMessage);
        Assert.Equal(990, repositoryThatKeepsAccountsInMemory.NumberOfConflictsBeforeTheLinkIsUsed);
    }

    [Fact]
    public async Task DatabaseFailureWhileReadingTheLinkIsNotHidden()
    {
        AddLockedAccountWithItsUnlockLinkToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.DatabaseIsDownWhenReadingTheLinks = true;

        await Assert.ThrowsAsync<InvalidOperationException>(UnlockAccountWithTheLinkOfTheseTestsAsync);
    }
}
