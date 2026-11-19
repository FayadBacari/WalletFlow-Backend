using System.Net;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Security;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Emails;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Api.RegistrationRequestTestValues;

namespace WalletFlow.Tests.Application;

// Vérifie l'envoi des e-mails de la file (GR Auth, GR Compte, GR Notif, SS §2, §9 et §11), avec de faux outils.
// Seuls les jetons et les textes sont les vrais : il faut comparer le jeton du lien et son empreinte en base.
public class EmailRequestProcessingServiceTests
{
    private const string AccountEmail = "utilisateur@exemple.test";
    private const string NewEmail = "nouvelle@exemple.test";

    private static readonly IPAddress IpAddress = IPAddress.Parse("203.0.113.7");
    private const string SupportPage = "https://walletflow.test/support";

    private readonly FakeUserAccountRepository accountRepository = new FakeUserAccountRepository();
    private readonly FakeSecurityEventRepository securityEventRepository = new FakeSecurityEventRepository();
    private readonly FakeEmailRequestRepository emailRequests = new FakeEmailRequestRepository();
    private readonly FakeAlertRepository alertRepository = new FakeAlertRepository();
    private readonly FakeSupportRequestRepository supportRepository;
    private readonly FakeDatabaseUnitOfWork unitOfWork;
    private readonly RandomSecureTokenGenerator tokenGenerator = new RandomSecureTokenGenerator();
    private readonly FakeEmailSender emailSender = new FakeEmailSender();
    private readonly FakeTimeProvider clock = new FakeTimeProvider();
    private readonly FrenchAccountChangeEmails changeEmails = new FrenchAccountChangeEmails();

    // Relie les faux accès à la fausse unité de travail, qui garde leurs changements ensemble.
    public EmailRequestProcessingServiceTests()
    {
        supportRepository = new FakeSupportRequestRepository(accountRepository.SavedUsers);
        unitOfWork = new FakeDatabaseUnitOfWork(accountRepository, securityEventRepository, emailRequests, alertRepository, supportRepository);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // Crée le service à tester, avec ses faux outils.
    private EmailRequestProcessingService CreateProcessingService()
    {
        var websiteSettings = new PublicWebsiteSettings("https://walletflow.test");

        return new EmailRequestProcessingService(
            emailRequests,
            unitOfWork,
            new SecurityJournalService(securityEventRepository, clock),
            emailSender,
            new LinkEmailPreparation(accountRepository, tokenGenerator, new FrenchAccountRecoveryEmails(), changeEmails, websiteSettings, clock),
            new NoticeEmailPreparation(accountRepository, new FrenchSecurityAlertEmails(), changeEmails, websiteSettings),
            new AlertAndSupportEmailPreparation(
                accountRepository, alertRepository, new FrenchAlertEmails(), supportRepository, new FrenchSupportEmails(), websiteSettings),
            clock);
    }

    // Ajoute à la fausse base un compte avec cette adresse.
    private User AddAccount(string emailAddress = AccountEmail, bool isEmailConfirmed = true)
    {
        var account = new User { Email = emailAddress, PasswordHash = "empreinte-de-test", IsEmailConfirmed = isEmailConfirmed };
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
            LockoutEndsAt = Now + AccountLockoutRules.CalculateLockoutDuration(lockoutCount)
        };
        accountRepository.SavedUsers.Add(account);

        return account;
    }

    // Met la demande dans la fausse file, puis fait un essai.
    private async Task<EmailRequestOutcome> QueueThenProcessAsync(EmailRequest request)
    {
        emailRequests.SavedRequests.Add(request);

        return await CreateProcessingService().ProcessAsync(request.Id);
    }

    // Fait un nouvel essai pour une demande déjà dans la file.
    private async Task<EmailRequestOutcome> ProcessAgainAsync(EmailRequest request)
    {
        return await CreateProcessingService().ProcessAsync(request.Id);
    }

    // --- La file

    // L'essai le plus en retard passe en premier ; une demande dont l'heure n'est pas venue attend.
    [Fact]
    public async Task NextRequestIsTheDueOneWaitingTheLongest()
    {
        var later = EmailRequest.ForNewDevice(Guid.NewGuid(), IpAddress, Now + TimeSpan.FromMinutes(1));
        var newer = EmailRequest.ForNewDevice(Guid.NewGuid(), IpAddress, Now - TimeSpan.FromMinutes(1));
        var older = EmailRequest.ForNewDevice(Guid.NewGuid(), IpAddress, Now - TimeSpan.FromMinutes(2));
        emailRequests.SavedRequests.AddRange(new[] { later, newer, older });

        Assert.Equal(older.Id, await CreateProcessingService().FindNextDueRequestIdAsync());
        emailRequests.SavedRequests.Remove(older);
        Assert.Equal(newer.Id, await CreateProcessingService().FindNextDueRequestIdAsync());
        emailRequests.SavedRequests.Remove(newer);
        Assert.Null(await CreateProcessingService().FindNextDueRequestIdAsync());
    }

    // Une demande déjà sortie de la file, par un autre passage ou avec son compte, n'envoie rien.
    [Fact]
    public async Task RequestNoLongerQueuedSendsNothing()
    {
        var outcome = await CreateProcessingService().ProcessAsync(Guid.NewGuid());

        Assert.IsType<EmailRequestOutcome.NoLongerQueued>(outcome);
        Assert.Empty(emailSender.SentEmails);
    }

    [Fact]
    public async Task SentEmailTakesItsRequestOutOfTheQueue()
    {
        AddAccount();

        var outcome = await QueueThenProcessAsync(EmailRequest.ForNewDevice(accountRepository.SavedUsers[0].Id, IpAddress, Now));

        Assert.IsType<EmailRequestOutcome.Finished>(outcome);
        Assert.Single(emailSender.SentEmails);
        Assert.Empty(emailRequests.SavedRequests);
    }

    // --- Essais ratés (SS §9)

    // Après un échec, l'essai suivant attend 1 min, 5 min, 30 min, puis 2 h. Le 5e échec abandonne la demande.
    [Fact]
    public async Task FailedTriesWaitLongerEachTimeThenTheFifthGivesUp()
    {
        var account = AddAccount();
        emailSender.AlwaysFails = true;
        var request = EmailRequest.ForPasswordChange(account.Id, IpAddress, Now);
        emailRequests.SavedRequests.Add(request);
        var waits = new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2) };

        for (var failureNumber = 1; failureNumber <= 4; failureNumber++)
        {
            var outcome = await ProcessAgainAsync(request);

            var retry = Assert.IsType<EmailRequestOutcome.WillRetry>(outcome);
            Assert.Equal(failureNumber, retry.FailedTryCount);
            Assert.Equal(Now + waits[failureNumber - 1], retry.NextTryAt);
            Assert.Equal(account.Id, retry.UserId);
            Assert.Equal(EmailRequestType.PasswordChange, retry.Type);
            Assert.Equal(failureNumber, Assert.Single(emailRequests.SavedRequests).FailedTryCount);
            Assert.Equal(Now + waits[failureNumber - 1], request.NextTryAt);
            clock.Advance(waits[failureNumber - 1]);
        }

        var lastOutcome = await ProcessAgainAsync(request);

        var givenUp = Assert.IsType<EmailRequestOutcome.GivenUp>(lastOutcome);
        Assert.Equal(account.Id, givenUp.UserId);
        Assert.Equal(EmailRequestType.PasswordChange, givenUp.Type);
        Assert.Empty(emailRequests.SavedRequests);
        Assert.Empty(emailSender.SentEmails);
    }

    // Une courte panne ne perd rien : l'essai suivant envoie l'e-mail, et la demande sort de la file.
    [Fact]
    public async Task EmailSentOnALaterTryLeavesTheQueue()
    {
        var account = AddAccount();
        var request = EmailRequest.ForPasswordChange(account.Id, IpAddress, Now);
        emailSender.AlwaysFails = true;
        await QueueThenProcessAsync(request);
        emailSender.AlwaysFails = false;
        clock.Advance(TimeSpan.FromMinutes(1));

        var outcome = await ProcessAgainAsync(request);

        Assert.IsType<EmailRequestOutcome.Finished>(outcome);
        Assert.Single(emailSender.SentEmails);
        Assert.Empty(emailRequests.SavedRequests);
    }

    // Le compte est effacé pendant l'essai : sa demande disparaît avec lui, et l'essai raté ne la fait pas revenir.
    [Fact]
    public async Task RequestRemovedDuringTheTryStaysRemoved()
    {
        var account = AddAccount();
        var request = EmailRequest.ForPasswordChange(account.Id, IpAddress, Now);
        emailSender.AlwaysFails = true;
        emailRequests.RemovedBeforeSave = request.Id;

        var outcome = await QueueThenProcessAsync(request);

        Assert.IsType<EmailRequestOutcome.NoLongerQueued>(outcome);
        Assert.Empty(emailRequests.SavedRequests);
    }

    // Une panne imprévue compte comme un essai raté : sinon, la même demande resterait en tête de la file.
    [Fact]
    public async Task UnexpectedFailureCountsAsAFailedTry()
    {
        var account = AddAccount();
        var request = EmailRequest.ForPasswordChange(account.Id, IpAddress, Now);
        emailRequests.SavedRequests.Add(request);

        var outcome = await CreateProcessingService().RecordUnexpectedFailureAsync(request.Id);

        var retry = Assert.IsType<EmailRequestOutcome.WillRetry>(outcome);
        Assert.Equal(1, retry.FailedTryCount);
        Assert.Equal(Now + TimeSpan.FromMinutes(1), request.NextTryAt);
        Assert.Null(await CreateProcessingService().FindNextDueRequestIdAsync());
    }

    // Une panne de la base n'est pas cachée : la tâche de fond la note dans le journal technique.
    [Fact]
    public async Task DatabaseFailureIsNotHiddenAndSendsNoEmail()
    {
        AddAccount();
        accountRepository.IsDown = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => QueueThenProcessAsync(EmailRequest.ForPasswordRecovery(AccountEmail, IpAddress, Now)));

        Assert.Empty(emailSender.SentEmails);
        Assert.Single(emailRequests.SavedRequests);
    }

    // --- Récupération : compte confirmé

    [Fact]
    public async Task ConfirmedAccountReceivesLinkWhoseTokenIsOnlyStoredAsItsHash()
    {
        var account = AddAccount();

        await QueueThenProcessAsync(EmailRequest.ForPasswordRecovery(AccountEmail, IpAddress, Now));

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(AccountEmail, email.RecipientAddress);
        Assert.Contains("https://walletflow.test/reset-password#token=", email.Body);

        var linkToken = ReadConfirmationToken(email.Body);
        var token = Assert.Single(accountRepository.SavedEmailTokens);
        Assert.Equal(EmailTokenType.PasswordRecovery, token.Type);
        Assert.Equal(account.Id, token.UserId);
        Assert.Equal(tokenGenerator.HashToken(linkToken), token.TokenHash);
        Assert.NotEqual(linkToken, token.TokenHash);
        Assert.Equal(Now.AddMinutes(30), token.ExpiresAt);
    }

    // SS §9 : l'e-mail dit quoi faire si ce n'est pas l'utilisateur qui a agi, et ne contient jamais de mot de passe.
    [Fact]
    public async Task RecoveryEmailSaysWhatToDoAndNeverContainsThePasswordHash()
    {
        var account = AddAccount();
        account.ReplacePasswordHash("empreinte-du-mot-de-passe-du-compte");

        await QueueThenProcessAsync(EmailRequest.ForPasswordRecovery(AccountEmail, IpAddress, Now));

        var body = emailSender.SentEmails[0].Body;
        Assert.Contains("ignore ce message", body);
        Assert.Contains("30 minutes", body);
        Assert.DoesNotContain("empreinte-du-mot-de-passe-du-compte", body);
    }

    [Fact]
    public async Task RecoveryIsJournaledOnceAsSuccessWithItsUserAndIpAddress()
    {
        var account = AddAccount();

        await QueueThenProcessAsync(EmailRequest.ForPasswordRecovery(AccountEmail, IpAddress, Now));

        var securityEvent = Assert.Single(securityEventRepository.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.PasswordRecovery, securityEvent.Type);
        Assert.Equal(SecurityEventResult.Success, securityEvent.Result);
        Assert.Equal(account.Id, securityEvent.UserId);
        Assert.Equal(IpAddress, securityEvent.IpAddress);
        Assert.Equal(Now, securityEvent.OccurredAt);
    }

    // SS §2 : le jeton est invalidé dès qu'un nouveau jeton du même type est demandé. Un lien d'un autre type reste valable.
    [Fact]
    public async Task NewRecoveryLinkReplacesTheOldOneAndKeepsTheLinkOfAnotherType()
    {
        var account = AddAccount();
        accountRepository.SavedEmailTokens.Add(new EmailToken { Type = EmailTokenType.PasswordRecovery, TokenHash = "ancienne-empreinte", UserId = account.Id });
        accountRepository.SavedEmailTokens.Add(new EmailToken { Type = EmailTokenType.AccountUnlock, TokenHash = "empreinte-de-deblocage", UserId = account.Id });

        await QueueThenProcessAsync(EmailRequest.ForPasswordRecovery(AccountEmail, IpAddress, Now));

        Assert.Equal(2, accountRepository.SavedEmailTokens.Count);
        Assert.DoesNotContain(accountRepository.SavedEmailTokens, token => token.TokenHash == "ancienne-empreinte");
        Assert.Contains(accountRepository.SavedEmailTokens, token => token.TokenHash == "empreinte-de-deblocage");
    }

    // Chaque essai crée son lien : le lien de l'essai raté, qui n'est jamais arrivé, ne marche plus.
    [Fact]
    public async Task EachTryCreatesItsOwnLink()
    {
        AddAccount();
        var request = EmailRequest.ForPasswordRecovery(AccountEmail, IpAddress, Now);
        emailSender.AlwaysFails = true;
        await QueueThenProcessAsync(request);
        var failedTryLink = Assert.Single(accountRepository.SavedEmailTokens).TokenHash;
        emailSender.AlwaysFails = false;
        clock.Advance(TimeSpan.FromMinutes(1));

        await ProcessAgainAsync(request);

        var link = Assert.Single(accountRepository.SavedEmailTokens);
        Assert.NotEqual(failedTryLink, link.TokenHash);
        Assert.Equal(tokenGenerator.HashToken(ReadConfirmationToken(Assert.Single(emailSender.SentEmails).Body)), link.TokenHash);
    }

    // --- Récupération : adresse inconnue ou non confirmée

    // Une adresse sans compte ne reçoit rien, et le journal garde une trace sans identifiant.
    [Fact]
    public async Task UnknownAddressReceivesNothingAndIsJournaledWithoutIdentifier()
    {
        var outcome = await QueueThenProcessAsync(EmailRequest.ForPasswordRecovery(AccountEmail, IpAddress, Now));

        Assert.IsType<EmailRequestOutcome.Finished>(outcome);
        Assert.Empty(emailSender.SentEmails);
        Assert.Empty(accountRepository.SavedEmailTokens);
        Assert.Empty(emailRequests.SavedRequests);

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

        await QueueThenProcessAsync(EmailRequest.ForPasswordRecovery(AccountEmail, IpAddress, Now));

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

    // --- Récupération : envois ratés

    // Un essai raté n'écrit rien au journal : la demande n'est pas finie.
    [Fact]
    public async Task FailedRecoveryTryIsNotJournaledYet()
    {
        var account = AddAccount();
        emailSender.AlwaysFails = true;

        var outcome = await QueueThenProcessAsync(EmailRequest.ForPasswordRecovery(AccountEmail, IpAddress, Now));

        Assert.Equal(account.Id, Assert.IsType<EmailRequestOutcome.WillRetry>(outcome).UserId);
        Assert.Empty(securityEventRepository.SavedSecurityEvents);
    }

    // Le dernier essai raté sort la demande de la file, et la récupération est notée comme un échec, une seule fois.
    [Fact]
    public async Task RecoveryGivenUpIsJournaledOnceAsFailureWithItsUser()
    {
        var account = AddAccount();
        emailSender.AlwaysFails = true;
        var request = EmailRequest.ForPasswordRecovery(AccountEmail, IpAddress, Now);
        emailRequests.SavedRequests.Add(request);

        for (var tryNumber = 1; tryNumber <= EmailRequestRules.MaximumTries; tryNumber++)
        {
            await ProcessAgainAsync(request);
            clock.Advance(TimeSpan.FromHours(2));
        }

        var securityEvent = Assert.Single(securityEventRepository.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.PasswordRecovery, securityEvent.Type);
        Assert.Equal(SecurityEventResult.Failure, securityEvent.Result);
        Assert.Equal(account.Id, securityEvent.UserId);
        Assert.Equal(IpAddress, securityEvent.IpAddress);
        Assert.Empty(emailRequests.SavedRequests);
    }

    // --- Déblocage

    [Fact]
    public async Task LockedAccountReceivesUnlockLinkWhoseTokenIsOnlyStoredAsItsHash()
    {
        var account = AddLockedAccount();

        await QueueThenProcessAsync(EmailRequest.ForAccountUnlock(account.Id, IpAddress, Now));

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(AccountEmail, email.RecipientAddress);
        Assert.Contains("https://walletflow.test/unlock-account#token=", email.Body);

        var linkToken = ReadConfirmationToken(email.Body);
        var token = Assert.Single(accountRepository.SavedEmailTokens);
        Assert.Equal(EmailTokenType.AccountUnlock, token.Type);
        Assert.Equal(account.Id, token.UserId);
        Assert.Equal(tokenGenerator.HashToken(linkToken), token.TokenHash);
        Assert.Equal(Now.AddHours(1), token.ExpiresAt);
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

        await QueueThenProcessAsync(EmailRequest.ForAccountUnlock(account.Id, IpAddress, Now));

        Assert.Contains(text, emailSender.SentEmails[0].Body);
    }

    // SS §9 : l'e-mail dit quoi faire si ce n'est pas l'utilisateur. GR Auth : le lien ne connecte pas.
    [Fact]
    public async Task UnlockEmailSaysWhatToDoIfItWasNotTheUser()
    {
        var account = AddLockedAccount();

        await QueueThenProcessAsync(EmailRequest.ForAccountUnlock(account.Id, IpAddress, Now));

        var body = emailSender.SentEmails[0].Body;
        Assert.Contains("Si ce n'est pas toi", body);
        Assert.Contains("Il ne te connecte pas", body);
        Assert.Contains("1 heure", body);
    }

    // Le blocage est déjà noté par la connexion : son e-mail n'ajoute rien au journal.
    [Fact]
    public async Task UnlockEmailAddsNothingToTheSecurityJournal()
    {
        var account = AddLockedAccount();

        await QueueThenProcessAsync(EmailRequest.ForAccountUnlock(account.Id, IpAddress, Now));

        Assert.Empty(securityEventRepository.SavedSecurityEvents);
    }

    [Fact]
    public async Task ErasedAccountReceivesNoUnlockEmailAndLeavesTheQueue()
    {
        var outcome = await QueueThenProcessAsync(EmailRequest.ForAccountUnlock(Guid.NewGuid(), IpAddress, Now));

        Assert.IsType<EmailRequestOutcome.Finished>(outcome);
        Assert.Empty(emailSender.SentEmails);
        Assert.Empty(accountRepository.SavedEmailTokens);
        Assert.Empty(emailRequests.SavedRequests);
    }

    // Le blocage a pu finir entre la demande et son essai : l'e-mail dirait alors quelque chose de faux.
    [Fact]
    public async Task AccountNoLongerLockedReceivesNoUnlockEmail()
    {
        var account = AddLockedAccount();
        var request = EmailRequest.ForAccountUnlock(account.Id, IpAddress, Now);
        clock.Advance(TimeSpan.FromMinutes(15));

        await QueueThenProcessAsync(request);

        Assert.Empty(emailSender.SentEmails);
        Assert.Empty(accountRepository.SavedEmailTokens);
        Assert.Empty(emailRequests.SavedRequests);
    }

    // --- Changement d'adresse

    // Une adresse libre reçoit le lien qui la confirme, valable 24 heures. Seule l'empreinte du jeton est gardée (SS §2).
    [Fact]
    public async Task FreeAddressReceivesTheLinkThatConfirmsIt()
    {
        var account = AddAccount();

        await QueueThenProcessAsync(EmailRequest.ForEmailChange(account.Id, NewEmail, IpAddress, Now));

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(NewEmail, email.RecipientAddress);
        Assert.Contains("https://walletflow.test/confirm-email-change#token=", email.Body);
        var link = Assert.Single(accountRepository.SavedEmailTokens);
        Assert.Equal(EmailTokenType.EmailChange, link.Type);
        Assert.Equal(NewEmail, link.NewEmail);
        Assert.Equal(tokenGenerator.HashToken(ReadConfirmationToken(email.Body)), link.TokenHash);
        Assert.Equal(Now + EmailTokenValidityDurations.EmailChangeLink, link.ExpiresAt);
        Assert.Equal(AccountEmail, account.Email);
    }

    // SS §2 : une adresse déjà utilisée ne reçoit aucun lien ; son propriétaire est prévenu.
    [Fact]
    public async Task TakenAddressReceivesAWarningAndNoLink()
    {
        var account = AddAccount();
        AddAccount(NewEmail);

        await QueueThenProcessAsync(EmailRequest.ForEmailChange(account.Id, NewEmail, IpAddress, Now));

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(NewEmail, email.RecipientAddress);
        Assert.Equal(changeEmails.CreateAddressAlreadyUsedEmail(NewEmail).Subject, email.Subject);
        Assert.DoesNotContain("#token=", email.Body);
        Assert.Empty(accountRepository.SavedEmailTokens);
    }

    // GR Compte : un nouveau lien remplace l'ancien.
    [Fact]
    public async Task NewEmailChangeLinkReplacesThePreviousOne()
    {
        var account = AddAccount();

        await QueueThenProcessAsync(EmailRequest.ForEmailChange(account.Id, "premiere@exemple.test", IpAddress, Now));
        await QueueThenProcessAsync(EmailRequest.ForEmailChange(account.Id, NewEmail, IpAddress, Now));

        Assert.Equal(NewEmail, Assert.Single(accountRepository.SavedEmailTokens).NewEmail);
    }

    // Le compte a été effacé depuis sa demande : rien ne part, et aucun lien n'est créé.
    [Fact]
    public async Task ErasedAccountReceivesNoEmailChangeLink()
    {
        await QueueThenProcessAsync(EmailRequest.ForEmailChange(Guid.NewGuid(), NewEmail, IpAddress, Now));

        Assert.Empty(emailSender.SentEmails);
        Assert.Empty(accountRepository.SavedEmailTokens);
    }

    // SS §9 : l'ancienne adresse est prévenue, sans que l'e-mail cite la nouvelle.
    [Fact]
    public async Task OldAddressIsWarnedWithoutTheNewOne()
    {
        var account = AddAccount(NewEmail);

        await QueueThenProcessAsync(EmailRequest.ForEmailChangedNotice(account.Id, AccountEmail, IpAddress, Now));

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(AccountEmail, email.RecipientAddress);
        Assert.DoesNotContain(NewEmail, email.Body);
        Assert.EndsWith("au support de WalletFlow :\n" + SupportPage, email.Body);
    }

    // --- Alertes de sécurité (GR Notif)

    // L'alerte part à l'adresse actuelle du compte, avec l'heure de Paris de la connexion, et dit quoi faire (SS §9).
    [Fact]
    public async Task NewDeviceAlertGivesTheParisTimeAndWhatToDo()
    {
        var account = AddAccount();

        await QueueThenProcessAsync(EmailRequest.ForNewDevice(account.Id, IpAddress, Now));

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(AccountEmail, email.RecipientAddress);
        Assert.Equal("Nouvelle connexion à ton compte WalletFlow", email.Subject);
        Assert.Contains("le 4 novembre 2026 à 13 h 00 (heure de Paris)", email.Body);
        Assert.Contains("change tout de suite ton mot de passe", email.Body);
        Assert.Contains("support", email.Body);
        Assert.DoesNotContain(IpAddress.ToString(), email.Body);
    }

    // L'heure est celle de la connexion, pas celle de l'essai qui finit par envoyer l'e-mail.
    [Fact]
    public async Task PasswordChangeAlertGivesTheTimeOfTheChangeEvenOnALaterTry()
    {
        var account = AddAccount();
        var request = EmailRequest.ForPasswordChange(account.Id, IpAddress, Now);
        clock.Advance(TimeSpan.FromHours(3));

        await QueueThenProcessAsync(request);

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(AccountEmail, email.RecipientAddress);
        Assert.Equal("Le mot de passe de ton compte WalletFlow a changé", email.Subject);
        Assert.Contains("le 4 novembre 2026 à 13 h 00 (heure de Paris)", email.Body);
        Assert.Contains("Mot de passe oublié", email.Body);
    }

    // Une alerte part à l'adresse du compte au moment de l'envoi : après un changement d'adresse, la nouvelle.
    [Fact]
    public async Task SecurityAlertGoesToTheCurrentAddressOfTheAccount()
    {
        var account = AddAccount();
        var request = EmailRequest.ForPasswordChange(account.Id, IpAddress, Now);
        account.ReplaceEmail(NewEmail);

        await QueueThenProcessAsync(request);

        Assert.Equal(NewEmail, Assert.Single(emailSender.SentEmails).RecipientAddress);
    }

    // Chaque alerte de sécurité part à l'adresse du compte, avec l'heure de ce qui s'est passé, et finit par l'adresse de la page
    // de support du site : c'est son seul lien (SS §9).
    [Theory]
    [InlineData(EmailRequestType.NewDevice, "Nouvelle connexion à ton compte WalletFlow")]
    [InlineData(EmailRequestType.PasswordChange, "Le mot de passe de ton compte WalletFlow a changé")]
    [InlineData(EmailRequestType.TwoFactorDeactivation, "La double authentification de ton compte WalletFlow est désactivée")]
    [InlineData(EmailRequestType.BackupCodeUse, "Un code de secours de ton compte WalletFlow a servi")]
    public async Task EachSecurityAlertEndsWithTheSupportPageOfTheWebsite(EmailRequestType type, string subject)
    {
        var account = AddAccount();

        await QueueThenProcessAsync(CreateSecurityAlertRequest(type, account.Id));

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(AccountEmail, email.RecipientAddress);
        Assert.Equal(subject, email.Subject);
        Assert.Contains("le 4 novembre 2026 à 13 h 00 (heure de Paris)", email.Body);
        Assert.EndsWith(" :\n" + SupportPage, email.Body);
        Assert.Equal(email.Body.IndexOf("https://", StringComparison.Ordinal), email.Body.LastIndexOf("https://", StringComparison.Ordinal));
        Assert.DoesNotContain(IpAddress.ToString(), email.Body);
    }

    [Theory]
    [InlineData(EmailRequestType.NewDevice)]
    [InlineData(EmailRequestType.PasswordChange)]
    [InlineData(EmailRequestType.TwoFactorDeactivation)]
    [InlineData(EmailRequestType.BackupCodeUse)]
    public async Task ErasedAccountReceivesNoSecurityAlert(EmailRequestType type)
    {
        var outcome = await QueueThenProcessAsync(CreateSecurityAlertRequest(type, Guid.NewGuid()));

        Assert.IsType<EmailRequestOutcome.Finished>(outcome);
        Assert.Empty(emailSender.SentEmails);
    }

    // Crée la demande d'une alerte de sécurité de ce type, pour ce compte, à l'heure de la fausse horloge.
    private EmailRequest CreateSecurityAlertRequest(EmailRequestType type, Guid accountId)
    {
        return type switch
        {
            EmailRequestType.NewDevice => EmailRequest.ForNewDevice(accountId, IpAddress, Now),
            EmailRequestType.PasswordChange => EmailRequest.ForPasswordChange(accountId, IpAddress, Now),
            EmailRequestType.TwoFactorDeactivation => EmailRequest.ForTwoFactorDeactivation(accountId, IpAddress, Now),
            EmailRequestType.BackupCodeUse => EmailRequest.ForBackupCodeUse(accountId, IpAddress, Now),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    // Les alertes de sécurité n'ajoutent rien au journal : la connexion et le changement y sont déjà.
    [Fact]
    public async Task SecurityAlertsAddNothingToTheSecurityJournal()
    {
        var account = AddAccount();

        await QueueThenProcessAsync(EmailRequest.ForNewDevice(account.Id, IpAddress, Now));
        await QueueThenProcessAsync(EmailRequest.ForPasswordChange(account.Id, IpAddress, Now));

        Assert.Equal(2, emailSender.SentEmails.Count);
        Assert.Empty(securityEventRepository.SavedSecurityEvents);
    }

    // --- Les alertes qu'on peut désactiver (GR Notif, DD §9.3)

    // Garde une catégorie, un sac et une connexion de ce compte dans les fausses bases reliées aux fausses alertes,
    // et une alerte de chaque type : leurs e-mails disent leur nom.
    private List<Alert> SaveOneAlertOfEachKind(User account)
    {
        var categories = new FakeCategoryRepository();
        var category = new Category { Name = "Loisirs", UserId = account.Id };
        categories.SavedCategories.Add(category);
        var bags = new FakeBagRepository();
        var bag = new Bag { Name = "Vacances", UserId = account.Id };
        bags.SavedBags.Add(bag);
        var connections = new FakeBankConnectionRepository();
        var connection = new BankConnection
        {
            ExternalConnectionId = "session-alerte",
            BankName = "Banque du Nord",
            AuthorizationExpiresAt = new DateTime(2026, 11, 10, 23, 30, 0, DateTimeKind.Utc),
            UserId = account.Id
        };
        connections.SavedConnections.Add(connection);
        alertRepository.Categories = categories;
        alertRepository.Bags = bags;
        alertRepository.Connections = connections;

        var alerts = new List<Alert>
        {
            Alert.ForBudget(account.Id, category.Id, new DateOnly(2026, 11, 1), 80, Now),
            Alert.ForSuggestions(account.Id, new DateOnly(2026, 10, 1), Now),
            Alert.ForBagGoal(account.Id, bag.Id, Now),
            Alert.ForBankAuthorization(account.Id, connection.Id, Now)
        };
        alertRepository.SavedAlerts.AddRange(alerts);

        return alerts;
    }

    // Chaque alerte part à l'adresse du compte, avec le nom de ce qu'elle vise, et sort de la file.
    [Fact]
    public async Task EachAlertEmailNamesWhatItIsAbout()
    {
        var account = AddAccount();
        var alerts = SaveOneAlertOfEachKind(account);

        foreach (var alert in alerts)
        {
            Assert.IsType<EmailRequestOutcome.Finished>(await QueueThenProcessAsync(EmailRequest.ForAlert(alert)));
        }

        var bodies = emailSender.SentEmails.Select(email => email.Body).ToList();
        Assert.All(emailSender.SentEmails, email => Assert.Equal(AccountEmail, email.RecipientAddress));
        Assert.Contains("« Loisirs »", bodies[0]);
        Assert.Contains("80 %", bodies[0]);
        Assert.Contains("d'octobre 2026", bodies[1]);
        Assert.Contains("« Vacances »", bodies[2]);
        Assert.Contains("« Banque du Nord »", bodies[3]);
        Assert.Contains("11 novembre 2026", bodies[3]);
        Assert.Empty(emailRequests.SavedRequests);
    }

    // Ce que dit l'e-mail est lu au moment de l'envoi : une catégorie renommée entre-temps porte son nouveau nom.
    [Fact]
    public async Task AlertEmailGivesTheNameAtTheTimeOfSending()
    {
        var account = AddAccount();
        var budgetAlert = SaveOneAlertOfEachKind(account)[0];
        var request = EmailRequest.ForAlert(budgetAlert);
        alertRepository.Categories?.SavedCategories.Single().Rename("Sorties");

        await QueueThenProcessAsync(request);

        Assert.Contains("« Sorties »", Assert.Single(emailSender.SentEmails).Body);
    }

    // Une alerte effacée depuis, avec sa catégorie ou son sac, n'a plus rien à dire : rien ne part, et la demande sort.
    [Fact]
    public async Task ErasedAlertSendsNothingAndLeavesTheQueue()
    {
        var account = AddAccount();
        var bagAlert = SaveOneAlertOfEachKind(account)[2];
        var request = EmailRequest.ForAlert(bagAlert);
        alertRepository.SavedAlerts.Remove(bagAlert);

        var outcome = await QueueThenProcessAsync(request);

        Assert.IsType<EmailRequestOutcome.Finished>(outcome);
        Assert.Empty(emailSender.SentEmails);
        Assert.Empty(emailRequests.SavedRequests);
    }

    [Fact]
    public async Task ErasedAccountReceivesNoAlert()
    {
        var account = AddAccount();
        var alert = SaveOneAlertOfEachKind(account)[1];
        accountRepository.SavedUsers.Clear();

        var outcome = await QueueThenProcessAsync(EmailRequest.ForAlert(alert));

        Assert.IsType<EmailRequestOutcome.Finished>(outcome);
        Assert.Empty(emailSender.SentEmails);
    }

    // Une alerte part à l'adresse du compte au moment de l'envoi, et ne laisse aucune trace au journal de sécurité.
    [Fact]
    public async Task AlertGoesToTheCurrentAddressWithoutAnyJournalEvent()
    {
        var account = AddAccount();
        var request = EmailRequest.ForAlert(SaveOneAlertOfEachKind(account)[2]);
        account.ReplaceEmail(NewEmail);

        await QueueThenProcessAsync(request);

        Assert.Equal(NewEmail, Assert.Single(emailSender.SentEmails).RecipientAddress);
        Assert.Empty(securityEventRepository.SavedSecurityEvents);
    }

    // Un envoi raté garde la demande pour un autre essai, comme tout autre e-mail (SS §9).
    [Fact]
    public async Task FailedAlertEmailIsTriedAgainLater()
    {
        var account = AddAccount();
        var request = EmailRequest.ForAlert(SaveOneAlertOfEachKind(account)[0]);
        emailSender.AlwaysFails = true;

        var outcome = await QueueThenProcessAsync(request);

        var retry = Assert.IsType<EmailRequestOutcome.WillRetry>(outcome);
        Assert.Equal(EmailRequestType.Alert, retry.Type);
        Assert.Equal(account.Id, retry.UserId);
        Assert.Equal(request, Assert.Single(emailRequests.SavedRequests));
    }

    // --- Lien du support qui désactive la double authentification (GR Admin, GR Auth)

    // Le lien part à l'adresse actuelle du compte, avec le lien du site et la page de support. Seule son empreinte est gardée,
    // pour 24 heures, sans aucun essai compté. Il remplace un lien plus ancien du même type.
    [Fact]
    public async Task SupportLinkGoesToTheAccountWithItsHashKeptForTwentyFourHours()
    {
        var account = AddAccount();
        account.ActivateTwoFactor("secret-chiffre", TwoFactorTestTools.OldStep);
        accountRepository.SavedEmailTokens.Add(new EmailToken
        {
            Type = EmailTokenType.TwoFactorDeactivation,
            TokenHash = "ancienne-empreinte",
            ExpiresAt = Now.AddHours(1),
            UserId = account.Id
        });

        await QueueThenProcessAsync(EmailRequest.ForTwoFactorDeactivationLink(account.Id, IpAddress, Now));

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(AccountEmail, email.RecipientAddress);
        Assert.Contains("https://walletflow.test/disable-two-factor#token=", email.Body);
        Assert.EndsWith(" :\n" + SupportPage, email.Body);
        var token = Assert.Single(accountRepository.SavedEmailTokens);
        Assert.Equal(
            (EmailTokenType.TwoFactorDeactivation, account.Id, tokenGenerator.HashToken(ReadConfirmationToken(email.Body)), Now.AddHours(24), (short)0),
            (token.Type, token.UserId, token.TokenHash, token.ExpiresAt, token.PasswordTryCount));
    }

    // Un compte effacé, ou dont la double authentification est déjà désactivée, ne reçoit rien : il n'y a plus rien à désactiver.
    [Fact]
    public async Task SupportLinkIsNotSentWhenThereIsNothingToDeactivate()
    {
        var accountWithoutTwoFactor = AddAccount();

        var withoutTwoFactor = await QueueThenProcessAsync(EmailRequest.ForTwoFactorDeactivationLink(accountWithoutTwoFactor.Id, IpAddress, Now));
        var erased = await QueueThenProcessAsync(EmailRequest.ForTwoFactorDeactivationLink(Guid.NewGuid(), IpAddress, Now));

        Assert.IsType<EmailRequestOutcome.Finished>(withoutTwoFactor);
        Assert.IsType<EmailRequestOutcome.Finished>(erased);
        Assert.Empty(emailSender.SentEmails);
        Assert.Empty(accountRepository.SavedEmailTokens);
    }

    // --- Réponse du support (GR Admin)

    // La réponse part avec son texte à l'adresse du compte au moment de l'envoi : après un changement d'adresse, la nouvelle.
    [Fact]
    public async Task SupportResponseGoesToTheCurrentAddressOfTheAccount()
    {
        var account = AddAccount();
        var request = SupportRequest.FromUser(account.Id, "Bonjour", Now.AddDays(-2));
        var response = SupportResponse.Create(request.Id, "Voici la marche à suivre.\nBonne journée.", Now);
        supportRepository.SavedRequests.Add(request);
        supportRepository.SavedResponses.Add(response);
        account.ReplaceEmail(NewEmail);

        await QueueThenProcessAsync(EmailRequest.ForSupportResponse(response, IpAddress));

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(NewEmail, email.RecipientAddress);
        Assert.Equal("Réponse du support de WalletFlow", email.Subject);
        Assert.Contains("Voici la marche à suivre.\nBonne journée.", email.Body);
        Assert.EndsWith(" :\n" + SupportPage, email.Body);
    }

    // Le jour de la demande est celui de Paris : une demande envoyée à 23 h 30 en UTC, un soir d'hiver, date du lendemain.
    [Fact]
    public async Task SupportResponseGivesTheDayOfTheRequestInParis()
    {
        var request = SupportRequest.FromVisitor("visiteur@exemple.test", "Bonjour", new DateTime(2026, 11, 14, 23, 30, 0, DateTimeKind.Utc));
        var response = SupportResponse.Create(request.Id, "Réponse", Now);
        supportRepository.SavedRequests.Add(request);
        supportRepository.SavedResponses.Add(response);

        await QueueThenProcessAsync(EmailRequest.ForSupportResponse(response, IpAddress));

        Assert.Contains("ta demande du 15 novembre 2026 :", Assert.Single(emailSender.SentEmails).Body);
    }

    // La réponse à un visiteur part à son adresse de contact.
    [Fact]
    public async Task SupportResponseToAVisitorGoesToItsContactAddress()
    {
        var request = SupportRequest.FromVisitor("visiteur@exemple.test", "Mon compte est bloqué.", Now.AddDays(-1));
        var response = SupportResponse.Create(request.Id, "Ton compte est débloqué.", Now);
        supportRepository.SavedRequests.Add(request);
        supportRepository.SavedResponses.Add(response);

        await QueueThenProcessAsync(EmailRequest.ForSupportResponse(response, IpAddress));

        Assert.Equal("visiteur@exemple.test", Assert.Single(emailSender.SentEmails).RecipientAddress);
    }

    // Une réponse effacée depuis, avec sa demande, ne part plus.
    [Fact]
    public async Task ErasedResponseIsNotSent()
    {
        var response = SupportResponse.Create(Guid.NewGuid(), "Réponse", Now);

        var outcome = await QueueThenProcessAsync(EmailRequest.ForSupportResponse(response, IpAddress));

        Assert.IsType<EmailRequestOutcome.Finished>(outcome);
        Assert.Empty(emailSender.SentEmails);
    }

    // --- Suppression du compte (GR Suppression)

    // La confirmation donne le jour de Paris de l'effacement : 30 jours après une demande faite à 23 h 30 UTC, un soir d'hiver,
    // c'est déjà le lendemain à Paris. Elle finit par l'adresse de la page de support.
    [Fact]
    public async Task DeletionRequestEmailGivesTheErasureDayInParis()
    {
        var account = AddAccount();
        account.RequestDeletion(new DateTime(2026, 11, 16, 23, 30, 0, DateTimeKind.Utc));

        await QueueThenProcessAsync(EmailRequest.ForDeletionRequest(account.Id, IpAddress, Now));

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal((AccountEmail, "La suppression de ton compte WalletFlow est demandée"), (email.RecipientAddress, email.Subject));
        Assert.Contains("effacés définitivement le 17 décembre 2026.", email.Body);
        Assert.EndsWith(" :\n" + SupportPage, email.Body);
    }

    // Une demande déjà annulée par une connexion, ou un compte déjà effacé, n'a plus de confirmation à recevoir.
    [Fact]
    public async Task DeletionRequestEmailIsNotSentOnceCancelledOrErased()
    {
        var cancelled = AddAccount();

        var cancelledOutcome = await QueueThenProcessAsync(EmailRequest.ForDeletionRequest(cancelled.Id, IpAddress, Now));
        var erasedOutcome = await QueueThenProcessAsync(EmailRequest.ForDeletionRequest(Guid.NewGuid(), IpAddress, Now));

        Assert.IsType<EmailRequestOutcome.Finished>(cancelledOutcome);
        Assert.IsType<EmailRequestOutcome.Finished>(erasedOutcome);
        Assert.Empty(emailSender.SentEmails);
    }

    // L'annulation est une alerte de sécurité : elle part à l'adresse du compte, avec le moment et la page de support.
    [Fact]
    public async Task DeletionCancellationAlertGoesToTheAccount()
    {
        var account = AddAccount();

        await QueueThenProcessAsync(EmailRequest.ForDeletionCancellation(account.Id, IpAddress, Now));

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal((AccountEmail, "La suppression de ton compte WalletFlow est annulée"), (email.RecipientAddress, email.Subject));
        Assert.EndsWith(" :\n" + SupportPage, email.Body);
    }

    // L'e-mail d'effacement part à l'adresse qu'il garde : le compte n'existe plus.
    [Fact]
    public async Task ErasureEmailGoesToTheKeptAddressWithoutAnAccount()
    {
        var outcome = await QueueThenProcessAsync(EmailRequest.ForAccountErasure("efface@exemple.test", Now));

        Assert.IsType<EmailRequestOutcome.Finished>(outcome);
        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(("efface@exemple.test", "Ton compte WalletFlow est effacé"), (email.RecipientAddress, email.Subject));
        Assert.Contains("il y a 30 jours", email.Body);
    }
}
