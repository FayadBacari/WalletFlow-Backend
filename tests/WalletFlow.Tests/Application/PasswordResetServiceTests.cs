using System.Net;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le choix d'un nouveau mot de passe avec le lien reçu (GR Auth, SS §2, §3 et §11), avec de faux outils.
public class PasswordResetServiceTests
{
    private const string NewPasswordChosenInTheseTests = "Nouveau-Mot-De-Passe-2";
    private const string TokenOfTheRecoveryLinkInTheseTests = FakeSecureTokenGenerator.TokenAlwaysReturnedByThisFakeGenerator;
    private const string OldHashOfTheAccountInTheseTests = "ancienne-empreinte-du-compte";

    private static readonly IPAddress IpAddressOfTheVisitorInTheseTests = IPAddress.Parse("203.0.113.7");

    private readonly FakeUserAccountRepository repositoryThatKeepsAccountsInMemory = new FakeUserAccountRepository();
    private readonly FakeUserSessionRepository repositoryThatKeepsSessionsInMemory = new FakeUserSessionRepository(new FakeSecurityEventRepository());
    private readonly FakePasswordHasher hasherThatCountsTheHashedPasswords = new FakePasswordHasher();
    private readonly FakeTimeProviderForTests clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();

    // Crée le service à tester, avec ses faux outils.
    private PasswordResetService CreatePasswordResetServiceWithFakeTools()
    {
        return new PasswordResetService(
            repositoryThatKeepsAccountsInMemory,
            repositoryThatKeepsSessionsInMemory,
            hasherThatCountsTheHashedPasswords,
            new FakeSecureTokenGenerator(),
            clockThatOnlyMovesWhenTestsAskIt);
    }

    // Ajoute à la fausse base un compte confirmé, avec son lien de récupération valable 30 minutes.
    private User AddAccountWithItsRecoveryLinkToTheFakeDatabase(bool emailAddressOfTheAccountIsConfirmed = true, EmailTokenType typeOfTheLinkOfTheAccount = EmailTokenType.PasswordRecovery)
    {
        var accountAddedToTheFakeDatabase = new User
        {
            Email = "utilisateur@exemple.test",
            PasswordHash = OldHashOfTheAccountInTheseTests,
            IsEmailConfirmed = emailAddressOfTheAccountIsConfirmed
        };
        repositoryThatKeepsAccountsInMemory.SavedUsers.Add(accountAddedToTheFakeDatabase);
        repositoryThatKeepsAccountsInMemory.SavedEmailTokens.Add(new EmailToken
        {
            Type = typeOfTheLinkOfTheAccount,
            TokenHash = FakeSecureTokenGenerator.HashAlwaysReturnedByThisFakeGenerator,
            ExpiresAt = clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddMinutes(30),
            UserId = accountAddedToTheFakeDatabase.Id
        });

        return accountAddedToTheFakeDatabase;
    }

    // Ajoute à la fausse base une session de ce compte.
    private void AddSessionOfThisAccountToTheFakeDatabase(Guid identifierOfTheAccountThatOwnsTheSession)
    {
        repositoryThatKeepsSessionsInMemory.SavedSessions.Add(new UserSession
        {
            Id = Guid.NewGuid().ToString(),
            LastActivityAt = clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime,
            UserId = identifierOfTheAccountThatOwnsTheSession
        });
    }

    // Envoie un nouveau mot de passe avec le lien de ces tests.
    private async Task<PasswordResetResult> ResetPasswordWithTheLinkOfTheseTestsAsync(string newPasswordTypedInThisTest = NewPasswordChosenInTheseTests)
    {
        return await CreatePasswordResetServiceWithFakeTools().ResetPasswordWithLinkTokenAsync(TokenOfTheRecoveryLinkInTheseTests, newPasswordTypedInThisTest, IpAddressOfTheVisitorInTheseTests);
    }

    // Vérifie que le lien a été refusé, et que rien n'a changé.
    private void CheckThatLinkWasRefusedAndNothingChanged(PasswordResetResult resultOfThePasswordResetToCheck, User accountThatMustNotHaveChanged)
    {
        Assert.False(resultOfThePasswordResetToCheck.IsPasswordChanged);
        Assert.Equal(PasswordResetService.ErrorMessageForRecoveryLinkNoLongerValid, resultOfThePasswordResetToCheck.LinkErrorMessage);
        Assert.Null(resultOfThePasswordResetToCheck.PasswordErrorMessage);
        Assert.Equal(OldHashOfTheAccountInTheseTests, accountThatMustNotHaveChanged.PasswordHash);
        Assert.Empty(repositoryThatKeepsAccountsInMemory.SecurityEventsSavedWithTheLinks);
    }

    // --- Changement réussi

    [Fact]
    public async Task RightLinkChangesThePasswordAndUsesTheLink()
    {
        var accountBeingTestedInThisReset = AddAccountWithItsRecoveryLinkToTheFakeDatabase();

        var resultOfThePasswordResetRequest = await ResetPasswordWithTheLinkOfTheseTestsAsync();

        Assert.True(resultOfThePasswordResetRequest.IsPasswordChanged);
        Assert.Equal(FakePasswordHasher.PrefixOfTheFakePasswordHashes + NewPasswordChosenInTheseTests, accountBeingTestedInThisReset.PasswordHash);
        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime, repositoryThatKeepsAccountsInMemory.SavedEmailTokens[0].UsedAt);
    }

    // GR Auth : une récupération réussie débloque aussi le compte, sans effacer le nombre de blocages.
    [Fact]
    public async Task SuccessfulResetUnlocksTheAccountAndKeepsTheNumberOfLockouts()
    {
        var accountBeingTestedInThisReset = AddAccountWithItsRecoveryLinkToTheFakeDatabase();
        accountBeingTestedInThisReset.FailedLoginAttemptCount = 5;
        accountBeingTestedInThisReset.LockoutCount = 3;
        accountBeingTestedInThisReset.LockoutEndsAt = clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddHours(1);

        await ResetPasswordWithTheLinkOfTheseTestsAsync();

        Assert.Equal(0, accountBeingTestedInThisReset.FailedLoginAttemptCount);
        Assert.Null(accountBeingTestedInThisReset.LockoutEndsAt);
        Assert.Equal(3, accountBeingTestedInThisReset.LockoutCount);
    }

    // SS §3 : après une récupération, toutes les sessions du compte sont fermées, et seulement les siennes.
    [Fact]
    public async Task SuccessfulResetClosesEverySessionOfTheAccountAndOnlyThose()
    {
        var accountBeingTestedInThisReset = AddAccountWithItsRecoveryLinkToTheFakeDatabase();
        var identifierOfAnotherAccountOfTheTest = Guid.NewGuid();
        AddSessionOfThisAccountToTheFakeDatabase(accountBeingTestedInThisReset.Id);
        AddSessionOfThisAccountToTheFakeDatabase(accountBeingTestedInThisReset.Id);
        AddSessionOfThisAccountToTheFakeDatabase(identifierOfAnotherAccountOfTheTest);

        await ResetPasswordWithTheLinkOfTheseTestsAsync();

        Assert.Equal(identifierOfAnotherAccountOfTheTest, Assert.Single(repositoryThatKeepsSessionsInMemory.SavedSessions).UserId);
    }

    // GR Auth : la récupération ne connecte personne. L'utilisateur se connecte avec son nouveau mot de passe.
    [Fact]
    public async Task SuccessfulResetOpensNoSessionForTheVisitor()
    {
        AddAccountWithItsRecoveryLinkToTheFakeDatabase();

        await ResetPasswordWithTheLinkOfTheseTestsAsync();

        Assert.Empty(repositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    [Fact]
    public async Task SuccessfulResetIsWrittenInTheJournalWithItsUserAndIpAddress()
    {
        var accountBeingTestedInThisReset = AddAccountWithItsRecoveryLinkToTheFakeDatabase();

        await ResetPasswordWithTheLinkOfTheseTestsAsync();

        var securityEventOfThePasswordChange = Assert.Single(repositoryThatKeepsAccountsInMemory.SecurityEventsSavedWithTheLinks);
        Assert.Equal(SecurityEventType.PasswordChange, securityEventOfThePasswordChange.Type);
        Assert.Equal(SecurityEventResult.Success, securityEventOfThePasswordChange.Result);
        Assert.Equal(accountBeingTestedInThisReset.Id, securityEventOfThePasswordChange.UserId);
        Assert.Equal(IpAddressOfTheVisitorInTheseTests, securityEventOfThePasswordChange.IpAddress);
        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime, securityEventOfThePasswordChange.OccurredAt);
    }

    // Aucune règle n'interdit de garder le même mot de passe : le lien a prouvé qu'il possède la boîte mail.
    [Fact]
    public async Task SamePasswordAsTheOldOneIsAccepted()
    {
        var accountBeingTestedInThisReset = AddAccountWithItsRecoveryLinkToTheFakeDatabase();
        accountBeingTestedInThisReset.PasswordHash = FakePasswordHasher.PrefixOfTheFakePasswordHashes + NewPasswordChosenInTheseTests;

        var resultOfThePasswordResetRequest = await ResetPasswordWithTheLinkOfTheseTestsAsync();

        Assert.True(resultOfThePasswordResetRequest.IsPasswordChanged);
    }

    // --- Règles du mot de passe

    [Theory]
    [InlineData("trop-court")]
    [InlineData("")]
    public async Task PasswordBreakingTheRulesIsRefusedAndKeepsTheLinkUsable(string newPasswordTypedInThisTest)
    {
        var accountBeingTestedInThisReset = AddAccountWithItsRecoveryLinkToTheFakeDatabase();
        AddSessionOfThisAccountToTheFakeDatabase(accountBeingTestedInThisReset.Id);

        var resultOfThePasswordResetRequest = await ResetPasswordWithTheLinkOfTheseTestsAsync(newPasswordTypedInThisTest);

        Assert.False(resultOfThePasswordResetRequest.IsPasswordChanged);
        Assert.Equal(AccountRegistrationService.ErrorMessageForPasswordBreakingTheRules, resultOfThePasswordResetRequest.PasswordErrorMessage);
        Assert.Null(resultOfThePasswordResetRequest.LinkErrorMessage);
        Assert.Equal(OldHashOfTheAccountInTheseTests, accountBeingTestedInThisReset.PasswordHash);
        Assert.Null(repositoryThatKeepsAccountsInMemory.SavedEmailTokens[0].UsedAt);
        Assert.Single(repositoryThatKeepsSessionsInMemory.SavedSessions);
        Assert.Equal(0, hasherThatCountsTheHashedPasswords.NumberOfPasswordsHashed);

        var resultWithAValidPasswordAfterwards = await ResetPasswordWithTheLinkOfTheseTestsAsync();
        Assert.True(resultWithAValidPasswordAfterwards.IsPasswordChanged);
    }

    // SS §2 : le mot de passe est normalisé avant les règles et le hachage.
    [Fact]
    public async Task PasswordTypedWithDecomposedAccentIsHashedInItsNormalizedForm()
    {
        var accountBeingTestedInThisReset = AddAccountWithItsRecoveryLinkToTheFakeDatabase();

        await ResetPasswordWithTheLinkOfTheseTestsAsync("Mot-De-Passé-1");

        Assert.Equal(FakePasswordHasher.PrefixOfTheFakePasswordHashes + "Mot-De-Passé-1", accountBeingTestedInThisReset.PasswordHash);
    }

    [Fact]
    public async Task PasswordLengthIsCountedAfterTheNormalization()
    {
        AddAccountWithItsRecoveryLinkToTheFakeDatabase();

        var resultOfThePasswordResetRequest = await ResetPasswordWithTheLinkOfTheseTestsAsync("Abcdefgh-1é");

        Assert.Equal(AccountRegistrationService.ErrorMessageForPasswordBreakingTheRules, resultOfThePasswordResetRequest.PasswordErrorMessage);
    }

    [Theory]
    [InlineData(0xFFFE)]
    [InlineData(0x200B)] // espace invisible
    public async Task PasswordWithForbiddenCharacterIsRefusedWithoutAnyError(int codeOfTheForbiddenCharacterInThisTest)
    {
        AddAccountWithItsRecoveryLinkToTheFakeDatabase();

        var resultOfThePasswordResetRequest = await ResetPasswordWithTheLinkOfTheseTestsAsync(NewPasswordChosenInTheseTests + (char)codeOfTheForbiddenCharacterInThisTest);

        Assert.Equal(AccountRegistrationService.ErrorMessageForPasswordBreakingTheRules, resultOfThePasswordResetRequest.PasswordErrorMessage);
    }

    // --- Liens refusés

    [Fact]
    public async Task UnknownLinkIsRefusedWithoutHashingThePassword()
    {
        var accountBeingTestedInThisReset = AddAccountWithItsRecoveryLinkToTheFakeDatabase();

        var resultOfThePasswordResetRequest = await CreatePasswordResetServiceWithFakeTools().ResetPasswordWithLinkTokenAsync("jeton-invente", NewPasswordChosenInTheseTests, IpAddressOfTheVisitorInTheseTests);

        CheckThatLinkWasRefusedAndNothingChanged(resultOfThePasswordResetRequest, accountBeingTestedInThisReset);
        Assert.Equal(0, hasherThatCountsTheHashedPasswords.NumberOfPasswordsHashed);
    }

    [Fact]
    public async Task LinkAtItsExactExpirationDateIsRefused()
    {
        var accountBeingTestedInThisReset = AddAccountWithItsRecoveryLinkToTheFakeDatabase();
        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(30));

        var resultOfThePasswordResetRequest = await ResetPasswordWithTheLinkOfTheseTestsAsync();

        CheckThatLinkWasRefusedAndNothingChanged(resultOfThePasswordResetRequest, accountBeingTestedInThisReset);
    }

    [Fact]
    public async Task LinkJustBeforeItsExpirationDateIsAccepted()
    {
        AddAccountWithItsRecoveryLinkToTheFakeDatabase();
        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(1));

        var resultOfThePasswordResetRequest = await ResetPasswordWithTheLinkOfTheseTestsAsync();

        Assert.True(resultOfThePasswordResetRequest.IsPasswordChanged);
    }

    [Fact]
    public async Task LinkAlreadyUsedOnceIsRefusedAgain()
    {
        AddAccountWithItsRecoveryLinkToTheFakeDatabase();
        await ResetPasswordWithTheLinkOfTheseTestsAsync();
        var securityEventsAfterTheFirstReset = repositoryThatKeepsAccountsInMemory.SecurityEventsSavedWithTheLinks.Count;

        var resultOfTheSecondPasswordReset = await ResetPasswordWithTheLinkOfTheseTestsAsync("Encore-Un-Mot-De-Passe-3");

        Assert.Equal(PasswordResetService.ErrorMessageForRecoveryLinkNoLongerValid, resultOfTheSecondPasswordReset.LinkErrorMessage);
        Assert.Equal(securityEventsAfterTheFirstReset, repositoryThatKeepsAccountsInMemory.SecurityEventsSavedWithTheLinks.Count);
    }

    // Un lien de déblocage ne doit jamais permettre de changer un mot de passe.
    [Theory]
    [InlineData(EmailTokenType.AccountUnlock)]
    [InlineData(EmailTokenType.SignupConfirmation)]
    [InlineData(EmailTokenType.EmailChange)]
    public async Task LinkOfAnotherTypeIsRefused(EmailTokenType typeOfTheLinkOfTheAccountInThisTest)
    {
        var accountBeingTestedInThisReset = AddAccountWithItsRecoveryLinkToTheFakeDatabase(typeOfTheLinkOfTheAccount: typeOfTheLinkOfTheAccountInThisTest);

        var resultOfThePasswordResetRequest = await ResetPasswordWithTheLinkOfTheseTestsAsync();

        CheckThatLinkWasRefusedAndNothingChanged(resultOfThePasswordResetRequest, accountBeingTestedInThisReset);
    }

    [Fact]
    public async Task LinkOfAnAccountNotYetConfirmedIsRefused()
    {
        var accountBeingTestedInThisReset = AddAccountWithItsRecoveryLinkToTheFakeDatabase(emailAddressOfTheAccountIsConfirmed: false);

        var resultOfThePasswordResetRequest = await ResetPasswordWithTheLinkOfTheseTestsAsync();

        CheckThatLinkWasRefusedAndNothingChanged(resultOfThePasswordResetRequest, accountBeingTestedInThisReset);
    }

    [Fact]
    public async Task LinkOfAnAccountThatNoLongerExistsIsRefused()
    {
        var accountBeingTestedInThisReset = AddAccountWithItsRecoveryLinkToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.SavedUsers.Clear();

        var resultOfThePasswordResetRequest = await ResetPasswordWithTheLinkOfTheseTestsAsync();

        CheckThatLinkWasRefusedAndNothingChanged(resultOfThePasswordResetRequest, accountBeingTestedInThisReset);
    }

    // --- Requêtes simultanées et pannes

    // Un essai de connexion a changé le compte au même instant : le changement de mot de passe doit quand même réussir.
    [Fact]
    public async Task ConflictTriesAgainAndStillChangesThePassword()
    {
        var accountBeingTestedInThisReset = AddAccountWithItsRecoveryLinkToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.NumberOfConflictsBeforeTheLinkIsUsed = 3;

        var resultOfThePasswordResetRequest = await ResetPasswordWithTheLinkOfTheseTestsAsync();

        Assert.True(resultOfThePasswordResetRequest.IsPasswordChanged);
        Assert.Equal(FakePasswordHasher.PrefixOfTheFakePasswordHashes + NewPasswordChosenInTheseTests, accountBeingTestedInThisReset.PasswordHash);
        Assert.Single(repositoryThatKeepsAccountsInMemory.SecurityEventsSavedWithTheLinks);
        Assert.Equal(1, hasherThatCountsTheHashedPasswords.NumberOfPasswordsHashed);
    }

    // Double clic : le lien vient de servir à une autre requête, et les sessions ne sont pas touchées une seconde fois.
    [Fact]
    public async Task LinkUsedByAnotherRequestJustBeforeTheSaveIsRefused()
    {
        var accountBeingTestedInThisReset = AddAccountWithItsRecoveryLinkToTheFakeDatabase();
        AddSessionOfThisAccountToTheFakeDatabase(accountBeingTestedInThisReset.Id);
        repositoryThatKeepsAccountsInMemory.LinkIsUsedByAnotherRequestJustBeforeTheSave = true;

        var resultOfThePasswordResetRequest = await ResetPasswordWithTheLinkOfTheseTestsAsync();

        CheckThatLinkWasRefusedAndNothingChanged(resultOfThePasswordResetRequest, accountBeingTestedInThisReset);
        Assert.Single(repositoryThatKeepsSessionsInMemory.SavedSessions);
    }

    [Fact]
    public async Task TooManyConflictsRefuseTheLinkAfterTenTries()
    {
        var accountBeingTestedInThisReset = AddAccountWithItsRecoveryLinkToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.NumberOfConflictsBeforeTheLinkIsUsed = 1000;

        var resultOfThePasswordResetRequest = await ResetPasswordWithTheLinkOfTheseTestsAsync();

        CheckThatLinkWasRefusedAndNothingChanged(resultOfThePasswordResetRequest, accountBeingTestedInThisReset);
        Assert.Equal(990, repositoryThatKeepsAccountsInMemory.NumberOfConflictsBeforeTheLinkIsUsed);
    }

    // Le seul moment non atomique : la panne n'est pas cachée, et la personne peut refaire une récupération.
    [Fact]
    public async Task DatabaseFailureWhileClosingTheSessionsIsNotHidden()
    {
        AddAccountWithItsRecoveryLinkToTheFakeDatabase();
        repositoryThatKeepsSessionsInMemory.DatabaseIsDownWhileClosingEverySession = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ResetPasswordWithTheLinkOfTheseTestsAsync());
    }

    [Fact]
    public async Task DatabaseFailureWhileReadingTheLinkIsNotHiddenAndChangesNothing()
    {
        var accountBeingTestedInThisReset = AddAccountWithItsRecoveryLinkToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.DatabaseIsDownWhenReadingTheLinks = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ResetPasswordWithTheLinkOfTheseTestsAsync());

        Assert.Equal(OldHashOfTheAccountInTheseTests, accountBeingTestedInThisReset.PasswordHash);
    }
}
