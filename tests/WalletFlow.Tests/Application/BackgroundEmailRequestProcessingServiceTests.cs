using System.Net;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Emails;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Application;

// Vérifie les e-mails de récupération et de déblocage (GR Auth, SS §2, §9 et §11), avec de faux outils.
// Seuls les jetons sont les vrais : il faut comparer le jeton du lien et son empreinte en base.
public class BackgroundEmailRequestProcessingServiceTests
{
    private const string AccountEmail = "utilisateur@exemple.test";

    private static readonly IPAddress IpAddress = IPAddress.Parse("203.0.113.7");

    private readonly FakeUserAccountRepository accountRepository = new FakeUserAccountRepository();
    private readonly FakeSecurityEventRepository securityEventRepository = new FakeSecurityEventRepository();
    private readonly RandomSecureTokenGenerator tokenGenerator = new RandomSecureTokenGenerator();
    private readonly FakeEmailSender emailSender = new FakeEmailSender();
    private readonly FakeTimeProvider clock = new FakeTimeProvider();

    // Crée le service à tester, avec ses faux outils.
    private BackgroundEmailRequestProcessingService CreateProcessingService()
    {
        return new BackgroundEmailRequestProcessingService(
            accountRepository,
            new SecurityJournalService(securityEventRepository, clock),
            tokenGenerator,
            emailSender,
            new FrenchAccountRecoveryEmails(),
            new PublicWebsiteSettings("https://walletflow.test"),
            clock);
    }

    // Ajoute à la fausse base un compte avec l'adresse de ces tests.
    private User AddAccount(bool isEmailConfirmed = true)
    {
        var account = new User { Email = AccountEmail, IsEmailConfirmed = isEmailConfirmed };
        accountRepository.SavedUsers.Add(account);

        return account;
    }

    // Ajoute à la fausse base un compte qui vient d'être bloqué, avec ce nombre de blocages.
    private User AddLockedAccount(short lockoutCount = 1)
    {
        var account = new User
        {
            Email = AccountEmail,
            IsEmailConfirmed = true,
            FailedLoginAttemptCount = 5,
            LockoutCount = lockoutCount,
            LockoutEndsAt = clock.GetUtcNow().UtcDateTime + AccountLockoutRules.CalculateLockoutDuration(lockoutCount)
        };
        accountRepository.SavedUsers.Add(account);

        return account;
    }

    // Traite une demande de récupération pour l'adresse de ces tests.
    private async Task<BackgroundEmailRequestResult> ProcessRecoveryRequestAsync()
    {
        return await CreateProcessingService().ProcessAsync(new BackgroundEmailRequest.PasswordRecovery
        {
            EmailAddress = AccountEmail,
            IpAddress = IpAddress
        });
    }

    // Traite une demande d'e-mail de déblocage pour ce compte.
    private async Task<BackgroundEmailRequestResult> ProcessUnlockRequestAsync(Guid accountId)
    {
        return await CreateProcessingService().ProcessAsync(new BackgroundEmailRequest.AccountUnlock
        {
            AccountId = accountId,
            IpAddress = IpAddress
        });
    }

    // --- Récupération : compte confirmé

    [Fact]
    public async Task ConfirmedAccountReceivesLinkWhoseTokenIsOnlyStoredAsItsHash()
    {
        var account = AddAccount();

        var result = await ProcessRecoveryRequestAsync();

        Assert.False(result.EmailCouldNotBeSent);
        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(AccountEmail, email.RecipientAddress);
        Assert.Contains("https://walletflow.test/reset-password#token=", email.Body);

        var linkToken = ReadConfirmationToken(email.Body);
        var token = Assert.Single(accountRepository.SavedEmailTokens);
        Assert.Equal(EmailTokenType.PasswordRecovery, token.Type);
        Assert.Equal(account.Id, token.UserId);
        Assert.Equal(tokenGenerator.HashToken(linkToken), token.TokenHash);
        Assert.NotEqual(linkToken, token.TokenHash);
    }

    [Fact]
    public async Task RecoveryLinkLastsExactlyThirtyMinutes()
    {
        AddAccount();

        await ProcessRecoveryRequestAsync();

        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddMinutes(30), accountRepository.SavedEmailTokens[0].ExpiresAt);
    }

    // SS §9 : l'e-mail dit quoi faire si ce n'est pas l'utilisateur qui a agi, et ne contient jamais de mot de passe.
    [Fact]
    public async Task RecoveryEmailSaysWhatToDoAndNeverContainsThePasswordHash()
    {
        var account = AddAccount();
        account.ReplacePasswordHash("empreinte-du-mot-de-passe-du-compte");

        await ProcessRecoveryRequestAsync();

        var body = emailSender.SentEmails[0].Body;
        Assert.Contains("ignore ce message", body);
        Assert.Contains("30 minutes", body);
        Assert.DoesNotContain("empreinte-du-mot-de-passe-du-compte", body);
    }

    [Fact]
    public async Task RecoveryRequestIsWrittenInTheJournalAsSuccessWithItsUserAndIpAddress()
    {
        var account = AddAccount();

        await ProcessRecoveryRequestAsync();

        var securityEvent = Assert.Single(securityEventRepository.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.PasswordRecovery, securityEvent.Type);
        Assert.Equal(SecurityEventResult.Success, securityEvent.Result);
        Assert.Equal(account.Id, securityEvent.UserId);
        Assert.Equal(IpAddress, securityEvent.IpAddress);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, securityEvent.OccurredAt);
    }

    // SS §2 : le jeton est invalidé dès qu'un nouveau jeton du même type est demandé. Un lien d'un autre type reste valable.
    [Fact]
    public async Task NewRecoveryLinkReplacesTheOldOneAndKeepsTheLinkOfAnotherType()
    {
        var account = AddAccount();
        accountRepository.SavedEmailTokens.Add(new EmailToken { Type = EmailTokenType.PasswordRecovery, TokenHash = "ancienne-empreinte", UserId = account.Id });
        accountRepository.SavedEmailTokens.Add(new EmailToken { Type = EmailTokenType.AccountUnlock, TokenHash = "empreinte-de-deblocage", UserId = account.Id });

        await ProcessRecoveryRequestAsync();

        Assert.Equal(2, accountRepository.SavedEmailTokens.Count);
        Assert.DoesNotContain(accountRepository.SavedEmailTokens, token => token.TokenHash == "ancienne-empreinte");
        Assert.Contains(accountRepository.SavedEmailTokens, token => token.TokenHash == "empreinte-de-deblocage");
    }

    // --- Récupération : adresse inconnue ou non confirmée

    // Une adresse sans compte ne reçoit rien, et le journal garde une trace sans identifiant.
    [Fact]
    public async Task UnknownAddressReceivesNothingAndIsWrittenInTheJournalWithoutIdentifier()
    {
        var result = await ProcessRecoveryRequestAsync();

        Assert.False(result.EmailCouldNotBeSent);
        Assert.Empty(emailSender.SentEmails);
        Assert.Empty(accountRepository.SavedEmailTokens);

        var securityEvent = Assert.Single(securityEventRepository.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.PasswordRecovery, securityEvent.Type);
        Assert.Equal(SecurityEventResult.Failure, securityEvent.Result);
        Assert.Null(securityEvent.UserId);
        Assert.Equal(IpAddress, securityEvent.IpAddress);
    }

    // GR Auth : l'e-mail de récupération invite à s'inscrire à nouveau, sans aucun lien.
    [Fact]
    public async Task UnconfirmedAddressIsInvitedToRegisterAgainWithoutAnyLink()
    {
        var account = AddAccount(isEmailConfirmed: false);

        await ProcessRecoveryRequestAsync();

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(AccountEmail, email.RecipientAddress);
        Assert.Contains("inscris-toi à nouveau", email.Body);
        Assert.Contains("ignore ce message", email.Body);
        Assert.DoesNotContain("#token=", email.Body);
        Assert.Empty(accountRepository.SavedEmailTokens);

        var securityEvent = Assert.Single(securityEventRepository.SavedSecurityEvents);
        Assert.Equal(SecurityEventResult.Failure, securityEvent.Result);
        Assert.Equal(account.Id, securityEvent.UserId);
    }

    // --- Récupération : pannes

    [Fact]
    public async Task EmailFailureOnConfirmedAccountIsReportedWithItsUserAndWrittenAsFailure()
    {
        var account = AddAccount();
        emailSender.AlwaysFails = true;

        var result = await ProcessRecoveryRequestAsync();

        Assert.True(result.EmailCouldNotBeSent);
        Assert.Equal(account.Id, result.UserId);
        Assert.Equal(SecurityEventResult.Failure, Assert.Single(securityEventRepository.SavedSecurityEvents).Result);
    }

    [Fact]
    public async Task EmailFailureOnUnconfirmedAccountIsReportedWithItsUser()
    {
        var account = AddAccount(isEmailConfirmed: false);
        emailSender.AlwaysFails = true;

        var result = await ProcessRecoveryRequestAsync();

        Assert.True(result.EmailCouldNotBeSent);
        Assert.Equal(account.Id, result.UserId);
    }

    // Une panne de la base n'est pas cachée : la tâche de fond la note dans le journal technique.
    [Fact]
    public async Task DatabaseFailureIsNotHiddenAndSendsNoEmail()
    {
        AddAccount();
        accountRepository.IsDown = true;

        await Assert.ThrowsAsync<InvalidOperationException>(ProcessRecoveryRequestAsync);

        Assert.Empty(emailSender.SentEmails);
    }

    // --- Déblocage

    [Fact]
    public async Task LockedAccountReceivesUnlockLinkWhoseTokenIsOnlyStoredAsItsHash()
    {
        var account = AddLockedAccount();

        var result = await ProcessUnlockRequestAsync(account.Id);

        Assert.False(result.EmailCouldNotBeSent);
        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(AccountEmail, email.RecipientAddress);
        Assert.Contains("https://walletflow.test/unlock-account#token=", email.Body);

        var linkToken = ReadConfirmationToken(email.Body);
        var token = Assert.Single(accountRepository.SavedEmailTokens);
        Assert.Equal(EmailTokenType.AccountUnlock, token.Type);
        Assert.Equal(account.Id, token.UserId);
        Assert.Equal(tokenGenerator.HashToken(linkToken), token.TokenHash);
    }

    [Fact]
    public async Task UnlockLinkLastsExactlyOneHour()
    {
        var account = AddLockedAccount();

        await ProcessUnlockRequestAsync(account.Id);

        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddHours(1), accountRepository.SavedEmailTokens[0].ExpiresAt);
    }

    [Theory]
    [InlineData(1, "pendant 15 minutes.")]
    [InlineData(2, "pendant 30 minutes.")]
    [InlineData(3, "pendant 1 heure.")]
    [InlineData(4, "pendant 2 heures.")]
    [InlineData(8, "pendant 24 heures.")]
    public async Task UnlockEmailGivesTheDurationOfTheLockout(short lockoutCount, string text)
    {
        var account = AddLockedAccount(lockoutCount);

        await ProcessUnlockRequestAsync(account.Id);

        Assert.Contains(text, emailSender.SentEmails[0].Body);
    }

    // SS §9 : l'e-mail dit quoi faire si ce n'est pas l'utilisateur. GR Auth : le lien ne connecte pas.
    [Fact]
    public async Task UnlockEmailSaysWhatToDoIfItWasNotTheUser()
    {
        var account = AddLockedAccount();

        await ProcessUnlockRequestAsync(account.Id);

        var body = emailSender.SentEmails[0].Body;
        Assert.Contains("Si ce n'est pas toi", body);
        Assert.Contains("Il ne te connecte pas", body);
        Assert.Contains("1 heure", body);
    }

    // Le blocage est déjà noté par la connexion : préparer son e-mail n'ajoute rien au journal.
    [Fact]
    public async Task UnlockEmailAddsNothingToTheSecurityJournal()
    {
        var account = AddLockedAccount();

        await ProcessUnlockRequestAsync(account.Id);

        Assert.Empty(securityEventRepository.SavedSecurityEvents);
    }

    [Fact]
    public async Task ErasedAccountReceivesNoUnlockEmail()
    {
        await ProcessUnlockRequestAsync(Guid.NewGuid());

        Assert.Empty(emailSender.SentEmails);
        Assert.Empty(accountRepository.SavedEmailTokens);
    }

    // Le blocage a pu finir entre la demande et son traitement : l'e-mail dirait alors quelque chose de faux.
    [Fact]
    public async Task AccountNoLongerLockedReceivesNoUnlockEmail()
    {
        var account = AddLockedAccount();
        clock.Advance(TimeSpan.FromMinutes(15));

        await ProcessUnlockRequestAsync(account.Id);

        Assert.Empty(emailSender.SentEmails);
        Assert.Empty(accountRepository.SavedEmailTokens);
    }

    [Fact]
    public async Task EmailFailureOnLockedAccountIsReportedWithItsUser()
    {
        var account = AddLockedAccount();
        emailSender.AlwaysFails = true;

        var result = await ProcessUnlockRequestAsync(account.Id);

        Assert.True(result.EmailCouldNotBeSent);
        Assert.Equal(account.Id, result.UserId);
    }
}
