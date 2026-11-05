using System.Net;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Configuration;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Application;

// Vérifie les e-mails de récupération et de déblocage (GR Auth, SS §2, §9 et §11), avec de faux outils.
// Seuls les jetons sont les vrais : il faut comparer le jeton du lien et son empreinte en base.
public class BackgroundEmailRequestProcessingServiceTests
{
    private const string EmailAddressOfTheAccountInTheseTests = "utilisateur@exemple.test";

    private static readonly IPAddress IpAddressOfTheVisitorInTheseTests = IPAddress.Parse("203.0.113.7");

    private readonly FakeUserAccountRepository repositoryThatKeepsAccountsInMemory = new FakeUserAccountRepository();
    private readonly FakeSecurityEventRepository repositoryThatKeepsSecurityEventsInMemory = new FakeSecurityEventRepository();
    private readonly RandomSecureTokenGenerator realGeneratorOfTheTokensInTheseTests = new RandomSecureTokenGenerator();
    private readonly FakeEmailSender senderThatKeepsTheEmailsInMemory = new FakeEmailSender();
    private readonly FakeTimeProviderForTests clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();

    // Crée le service à tester, avec ses faux outils.
    private BackgroundEmailRequestProcessingService CreateProcessingServiceWithFakeTools()
    {
        return new BackgroundEmailRequestProcessingService(
            repositoryThatKeepsAccountsInMemory,
            repositoryThatKeepsSecurityEventsInMemory,
            realGeneratorOfTheTokensInTheseTests,
            senderThatKeepsTheEmailsInMemory,
            new PublicWebsiteSettings("https://walletflow.test"),
            clockThatOnlyMovesWhenTestsAskIt);
    }

    // Ajoute à la fausse base un compte avec l'adresse de ces tests.
    private User AddAccountToTheFakeDatabase(bool emailAddressOfTheAccountIsConfirmed = true)
    {
        var accountAddedToTheFakeDatabase = new User { Email = EmailAddressOfTheAccountInTheseTests, IsEmailConfirmed = emailAddressOfTheAccountIsConfirmed };
        repositoryThatKeepsAccountsInMemory.SavedUsers.Add(accountAddedToTheFakeDatabase);

        return accountAddedToTheFakeDatabase;
    }

    // Ajoute à la fausse base un compte qui vient d'être bloqué, avec ce nombre de blocages.
    private User AddLockedAccountToTheFakeDatabase(short numberOfLockoutsOfTheAccount = 1)
    {
        var accountAddedToTheFakeDatabase = AddAccountToTheFakeDatabase();
        accountAddedToTheFakeDatabase.FailedLoginAttemptCount = 5;
        accountAddedToTheFakeDatabase.LockoutCount = numberOfLockoutsOfTheAccount;
        accountAddedToTheFakeDatabase.LockoutEndsAt = clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime
            + AccountLockoutRules.CalculateDurationOfTheLockoutWithThisNumber(numberOfLockoutsOfTheAccount);

        return accountAddedToTheFakeDatabase;
    }

    // Traite une demande de récupération pour l'adresse de ces tests.
    private async Task<BackgroundEmailRequestResult> ProcessPasswordRecoveryRequestOfThisTestAsync()
    {
        return await CreateProcessingServiceWithFakeTools().ProcessOneBackgroundEmailRequestAsync(new BackgroundEmailRequest
        {
            TypeOfTheRequestedBackgroundEmail = BackgroundEmailRequestType.PasswordRecovery,
            EmailAddressOfTheRecoveryRequest = EmailAddressOfTheAccountInTheseTests,
            IpAddressOfTheRequestingVisitor = IpAddressOfTheVisitorInTheseTests
        });
    }

    // Traite une demande d'e-mail de déblocage pour ce compte.
    private async Task<BackgroundEmailRequestResult> ProcessAccountUnlockRequestForThisAccountAsync(Guid identifierOfTheAccountJustLocked)
    {
        return await CreateProcessingServiceWithFakeTools().ProcessOneBackgroundEmailRequestAsync(new BackgroundEmailRequest
        {
            TypeOfTheRequestedBackgroundEmail = BackgroundEmailRequestType.AccountUnlock,
            IdentifierOfTheAccountJustLocked = identifierOfTheAccountJustLocked,
            IpAddressOfTheRequestingVisitor = IpAddressOfTheVisitorInTheseTests
        });
    }

    // --- Récupération : compte confirmé

    [Fact]
    public async Task ConfirmedAccountReceivesLinkWhoseTokenIsOnlyStoredAsItsHash()
    {
        var accountBeingTestedInThisRequest = AddAccountToTheFakeDatabase();

        var resultOfTheBackgroundEmailRequest = await ProcessPasswordRecoveryRequestOfThisTestAsync();

        Assert.False(resultOfTheBackgroundEmailRequest.EmailCouldNotBeSent);
        var emailSentToTheAddressOwner = Assert.Single(senderThatKeepsTheEmailsInMemory.SentEmails);
        Assert.Equal(EmailAddressOfTheAccountInTheseTests, emailSentToTheAddressOwner.RecipientAddress);
        Assert.Contains("https://walletflow.test/reset-password#token=", emailSentToTheAddressOwner.Body);

        var tokenReadInTheLinkOfTheEmail = ReadTokenWrittenInTheConfirmationLink(emailSentToTheAddressOwner.Body);
        var tokenSavedInTheFakeDatabase = Assert.Single(repositoryThatKeepsAccountsInMemory.SavedEmailTokens);
        Assert.Equal(EmailTokenType.PasswordRecovery, tokenSavedInTheFakeDatabase.Type);
        Assert.Equal(accountBeingTestedInThisRequest.Id, tokenSavedInTheFakeDatabase.UserId);
        Assert.Equal(realGeneratorOfTheTokensInTheseTests.ConvertSecureTokenIntoItsHash(tokenReadInTheLinkOfTheEmail), tokenSavedInTheFakeDatabase.TokenHash);
        Assert.NotEqual(tokenReadInTheLinkOfTheEmail, tokenSavedInTheFakeDatabase.TokenHash);
    }

    [Fact]
    public async Task RecoveryLinkLastsExactlyThirtyMinutes()
    {
        AddAccountToTheFakeDatabase();

        await ProcessPasswordRecoveryRequestOfThisTestAsync();

        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddMinutes(30), repositoryThatKeepsAccountsInMemory.SavedEmailTokens[0].ExpiresAt);
    }

    // SS §9 : l'e-mail dit quoi faire si ce n'est pas l'utilisateur qui a agi, et ne contient jamais de mot de passe.
    [Fact]
    public async Task RecoveryEmailSaysWhatToDoAndNeverContainsThePasswordHash()
    {
        var accountBeingTestedInThisRequest = AddAccountToTheFakeDatabase();
        accountBeingTestedInThisRequest.PasswordHash = "empreinte-du-mot-de-passe-du-compte";

        await ProcessPasswordRecoveryRequestOfThisTestAsync();

        var bodyOfTheEmailSentToTheOwner = senderThatKeepsTheEmailsInMemory.SentEmails[0].Body;
        Assert.Contains("ignore ce message", bodyOfTheEmailSentToTheOwner);
        Assert.Contains("30 minutes", bodyOfTheEmailSentToTheOwner);
        Assert.DoesNotContain("empreinte-du-mot-de-passe-du-compte", bodyOfTheEmailSentToTheOwner);
    }

    [Fact]
    public async Task RecoveryRequestIsWrittenInTheJournalAsSuccessWithItsUserAndIpAddress()
    {
        var accountBeingTestedInThisRequest = AddAccountToTheFakeDatabase();

        await ProcessPasswordRecoveryRequestOfThisTestAsync();

        var securityEventOfTheRecoveryRequest = Assert.Single(repositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.PasswordRecovery, securityEventOfTheRecoveryRequest.Type);
        Assert.Equal(SecurityEventResult.Success, securityEventOfTheRecoveryRequest.Result);
        Assert.Equal(accountBeingTestedInThisRequest.Id, securityEventOfTheRecoveryRequest.UserId);
        Assert.Equal(IpAddressOfTheVisitorInTheseTests, securityEventOfTheRecoveryRequest.IpAddress);
        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime, securityEventOfTheRecoveryRequest.OccurredAt);
    }

    // SS §2 : le jeton est invalidé dès qu'un nouveau jeton du même type est demandé. Un lien d'un autre type reste valable.
    [Fact]
    public async Task NewRecoveryLinkReplacesTheOldOneAndKeepsTheLinkOfAnotherType()
    {
        var accountBeingTestedInThisRequest = AddAccountToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.SavedEmailTokens.Add(new EmailToken { Type = EmailTokenType.PasswordRecovery, TokenHash = "ancienne-empreinte", UserId = accountBeingTestedInThisRequest.Id });
        repositoryThatKeepsAccountsInMemory.SavedEmailTokens.Add(new EmailToken { Type = EmailTokenType.AccountUnlock, TokenHash = "empreinte-de-deblocage", UserId = accountBeingTestedInThisRequest.Id });

        await ProcessPasswordRecoveryRequestOfThisTestAsync();

        Assert.Equal(2, repositoryThatKeepsAccountsInMemory.SavedEmailTokens.Count);
        Assert.DoesNotContain(repositoryThatKeepsAccountsInMemory.SavedEmailTokens, tokenKeptInTheFakeDatabase => tokenKeptInTheFakeDatabase.TokenHash == "ancienne-empreinte");
        Assert.Contains(repositoryThatKeepsAccountsInMemory.SavedEmailTokens, tokenKeptInTheFakeDatabase => tokenKeptInTheFakeDatabase.TokenHash == "empreinte-de-deblocage");
    }

    // --- Récupération : adresse inconnue ou non confirmée

    // Une adresse sans compte ne reçoit rien, et le journal garde une trace sans identifiant.
    [Fact]
    public async Task UnknownAddressReceivesNothingAndIsWrittenInTheJournalWithoutIdentifier()
    {
        var resultOfTheBackgroundEmailRequest = await ProcessPasswordRecoveryRequestOfThisTestAsync();

        Assert.False(resultOfTheBackgroundEmailRequest.EmailCouldNotBeSent);
        Assert.Empty(senderThatKeepsTheEmailsInMemory.SentEmails);
        Assert.Empty(repositoryThatKeepsAccountsInMemory.SavedEmailTokens);

        var securityEventOfTheRecoveryRequest = Assert.Single(repositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.PasswordRecovery, securityEventOfTheRecoveryRequest.Type);
        Assert.Equal(SecurityEventResult.Failure, securityEventOfTheRecoveryRequest.Result);
        Assert.Null(securityEventOfTheRecoveryRequest.UserId);
        Assert.Equal(IpAddressOfTheVisitorInTheseTests, securityEventOfTheRecoveryRequest.IpAddress);
    }

    // GR Auth : l'e-mail de récupération invite à s'inscrire à nouveau, sans aucun lien.
    [Fact]
    public async Task UnconfirmedAddressIsInvitedToRegisterAgainWithoutAnyLink()
    {
        var accountBeingTestedInThisRequest = AddAccountToTheFakeDatabase(emailAddressOfTheAccountIsConfirmed: false);

        await ProcessPasswordRecoveryRequestOfThisTestAsync();

        var emailSentToTheAddressOwner = Assert.Single(senderThatKeepsTheEmailsInMemory.SentEmails);
        Assert.Equal(EmailAddressOfTheAccountInTheseTests, emailSentToTheAddressOwner.RecipientAddress);
        Assert.Contains("inscris-toi à nouveau", emailSentToTheAddressOwner.Body);
        Assert.Contains("ignore ce message", emailSentToTheAddressOwner.Body);
        Assert.DoesNotContain("#token=", emailSentToTheAddressOwner.Body);
        Assert.Empty(repositoryThatKeepsAccountsInMemory.SavedEmailTokens);

        var securityEventOfTheRecoveryRequest = Assert.Single(repositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
        Assert.Equal(SecurityEventResult.Failure, securityEventOfTheRecoveryRequest.Result);
        Assert.Equal(accountBeingTestedInThisRequest.Id, securityEventOfTheRecoveryRequest.UserId);
    }

    // --- Récupération : pannes

    [Fact]
    public async Task EmailFailureOnConfirmedAccountIsReportedWithItsUserAndWrittenAsFailure()
    {
        var accountBeingTestedInThisRequest = AddAccountToTheFakeDatabase();
        senderThatKeepsTheEmailsInMemory.EveryEmailFailsDuringThisTest = true;

        var resultOfTheBackgroundEmailRequest = await ProcessPasswordRecoveryRequestOfThisTestAsync();

        Assert.True(resultOfTheBackgroundEmailRequest.EmailCouldNotBeSent);
        Assert.Equal(accountBeingTestedInThisRequest.Id, resultOfTheBackgroundEmailRequest.UserIdentifierForTheTechnicalLog);
        Assert.Equal(SecurityEventResult.Failure, Assert.Single(repositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents).Result);
    }

    [Fact]
    public async Task EmailFailureOnUnconfirmedAccountIsReportedWithItsUser()
    {
        var accountBeingTestedInThisRequest = AddAccountToTheFakeDatabase(emailAddressOfTheAccountIsConfirmed: false);
        senderThatKeepsTheEmailsInMemory.EveryEmailFailsDuringThisTest = true;

        var resultOfTheBackgroundEmailRequest = await ProcessPasswordRecoveryRequestOfThisTestAsync();

        Assert.True(resultOfTheBackgroundEmailRequest.EmailCouldNotBeSent);
        Assert.Equal(accountBeingTestedInThisRequest.Id, resultOfTheBackgroundEmailRequest.UserIdentifierForTheTechnicalLog);
    }

    // Une panne de la base n'est pas cachée : la tâche de fond la note dans le journal technique.
    [Fact]
    public async Task DatabaseFailureIsNotHiddenAndSendsNoEmail()
    {
        AddAccountToTheFakeDatabase();
        repositoryThatKeepsAccountsInMemory.DatabaseIsDownDuringThisTest = true;

        await Assert.ThrowsAsync<InvalidOperationException>(ProcessPasswordRecoveryRequestOfThisTestAsync);

        Assert.Empty(senderThatKeepsTheEmailsInMemory.SentEmails);
    }

    // --- Déblocage

    [Fact]
    public async Task LockedAccountReceivesUnlockLinkWhoseTokenIsOnlyStoredAsItsHash()
    {
        var accountBeingTestedInThisRequest = AddLockedAccountToTheFakeDatabase();

        var resultOfTheBackgroundEmailRequest = await ProcessAccountUnlockRequestForThisAccountAsync(accountBeingTestedInThisRequest.Id);

        Assert.False(resultOfTheBackgroundEmailRequest.EmailCouldNotBeSent);
        var emailSentToTheAddressOwner = Assert.Single(senderThatKeepsTheEmailsInMemory.SentEmails);
        Assert.Equal(EmailAddressOfTheAccountInTheseTests, emailSentToTheAddressOwner.RecipientAddress);
        Assert.Contains("https://walletflow.test/unlock-account#token=", emailSentToTheAddressOwner.Body);

        var tokenReadInTheLinkOfTheEmail = ReadTokenWrittenInTheConfirmationLink(emailSentToTheAddressOwner.Body);
        var tokenSavedInTheFakeDatabase = Assert.Single(repositoryThatKeepsAccountsInMemory.SavedEmailTokens);
        Assert.Equal(EmailTokenType.AccountUnlock, tokenSavedInTheFakeDatabase.Type);
        Assert.Equal(accountBeingTestedInThisRequest.Id, tokenSavedInTheFakeDatabase.UserId);
        Assert.Equal(realGeneratorOfTheTokensInTheseTests.ConvertSecureTokenIntoItsHash(tokenReadInTheLinkOfTheEmail), tokenSavedInTheFakeDatabase.TokenHash);
    }

    [Fact]
    public async Task UnlockLinkLastsExactlyOneHour()
    {
        var accountBeingTestedInThisRequest = AddLockedAccountToTheFakeDatabase();

        await ProcessAccountUnlockRequestForThisAccountAsync(accountBeingTestedInThisRequest.Id);

        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddHours(1), repositoryThatKeepsAccountsInMemory.SavedEmailTokens[0].ExpiresAt);
    }

    [Theory]
    [InlineData(1, "pendant 15 minutes.")]
    [InlineData(2, "pendant 30 minutes.")]
    [InlineData(3, "pendant 1 heure.")]
    [InlineData(4, "pendant 2 heures.")]
    [InlineData(8, "pendant 24 heures.")]
    public async Task UnlockEmailGivesTheDurationOfTheLockout(short numberOfLockoutsOfTheAccount, string textExpectedInTheUnlockEmail)
    {
        var accountBeingTestedInThisRequest = AddLockedAccountToTheFakeDatabase(numberOfLockoutsOfTheAccount);

        await ProcessAccountUnlockRequestForThisAccountAsync(accountBeingTestedInThisRequest.Id);

        Assert.Contains(textExpectedInTheUnlockEmail, senderThatKeepsTheEmailsInMemory.SentEmails[0].Body);
    }

    // SS §9 : l'e-mail dit quoi faire si ce n'est pas l'utilisateur. GR Auth : le lien ne connecte pas.
    [Fact]
    public async Task UnlockEmailSaysWhatToDoIfItWasNotTheUser()
    {
        var accountBeingTestedInThisRequest = AddLockedAccountToTheFakeDatabase();

        await ProcessAccountUnlockRequestForThisAccountAsync(accountBeingTestedInThisRequest.Id);

        var bodyOfTheEmailSentToTheOwner = senderThatKeepsTheEmailsInMemory.SentEmails[0].Body;
        Assert.Contains("Si ce n'est pas toi", bodyOfTheEmailSentToTheOwner);
        Assert.Contains("Il ne te connecte pas", bodyOfTheEmailSentToTheOwner);
        Assert.Contains("1 heure", bodyOfTheEmailSentToTheOwner);
    }

    // Le blocage est déjà noté par la connexion : préparer son e-mail n'ajoute rien au journal.
    [Fact]
    public async Task UnlockEmailAddsNothingToTheSecurityJournal()
    {
        var accountBeingTestedInThisRequest = AddLockedAccountToTheFakeDatabase();

        await ProcessAccountUnlockRequestForThisAccountAsync(accountBeingTestedInThisRequest.Id);

        Assert.Empty(repositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
    }

    [Fact]
    public async Task ErasedAccountReceivesNoUnlockEmail()
    {
        await ProcessAccountUnlockRequestForThisAccountAsync(Guid.NewGuid());

        Assert.Empty(senderThatKeepsTheEmailsInMemory.SentEmails);
        Assert.Empty(repositoryThatKeepsAccountsInMemory.SavedEmailTokens);
    }

    // Le blocage a pu finir entre la demande et son traitement : l'e-mail dirait alors quelque chose de faux.
    [Fact]
    public async Task AccountNoLongerLockedReceivesNoUnlockEmail()
    {
        var accountBeingTestedInThisRequest = AddLockedAccountToTheFakeDatabase();
        clockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(15));

        await ProcessAccountUnlockRequestForThisAccountAsync(accountBeingTestedInThisRequest.Id);

        Assert.Empty(senderThatKeepsTheEmailsInMemory.SentEmails);
        Assert.Empty(repositoryThatKeepsAccountsInMemory.SavedEmailTokens);
    }

    [Fact]
    public async Task EmailFailureOnLockedAccountIsReportedWithItsUser()
    {
        var accountBeingTestedInThisRequest = AddLockedAccountToTheFakeDatabase();
        senderThatKeepsTheEmailsInMemory.EveryEmailFailsDuringThisTest = true;

        var resultOfTheBackgroundEmailRequest = await ProcessAccountUnlockRequestForThisAccountAsync(accountBeingTestedInThisRequest.Id);

        Assert.True(resultOfTheBackgroundEmailRequest.EmailCouldNotBeSent);
        Assert.Equal(accountBeingTestedInThisRequest.Id, resultOfTheBackgroundEmailRequest.UserIdentifierForTheTechnicalLog);
    }
}
