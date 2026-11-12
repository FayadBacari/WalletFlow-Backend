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

// Vérifie les e-mails du changement d'adresse, préparés en arrière-plan (GR Compte, SS §2 et §9), avec de faux outils.
public class EmailChangeBackgroundEmailsTests
{
    private const string AccountEmail = "utilisateur@exemple.test";
    private const string NewEmail = "nouvelle@exemple.test";

    private static readonly IPAddress IpAddress = IPAddress.Parse("203.0.113.7");

    private readonly FakeUserAccountRepository accountRepository = new FakeUserAccountRepository();
    private readonly FakeSecurityEventRepository securityEventRepository = new FakeSecurityEventRepository();
    private readonly RandomSecureTokenGenerator tokenGenerator = new RandomSecureTokenGenerator();
    private readonly FakeEmailSender emailSender = new FakeEmailSender();
    private readonly FakeTimeProvider clock = new FakeTimeProvider();
    private readonly FrenchAccountChangeEmails changeEmails = new FrenchAccountChangeEmails();

    // Crée le service à tester, avec ses faux outils.
    private BackgroundEmailRequestProcessingService CreateProcessingService()
    {
        return new BackgroundEmailRequestProcessingService(
            accountRepository,
            new SecurityJournalService(securityEventRepository, clock),
            tokenGenerator,
            emailSender,
            new FrenchAccountRecoveryEmails(),
            changeEmails,
            new PublicWebsiteSettings("https://walletflow.test"),
            clock);
    }

    // Ajoute à la fausse base un compte confirmé avec cette adresse.
    private User AddAccount(string emailAddress)
    {
        var account = new User { Email = emailAddress, PasswordHash = "empreinte-de-test", IsEmailConfirmed = true };
        accountRepository.SavedUsers.Add(account);

        return account;
    }

    // Traite la demande d'une nouvelle adresse pour ce compte.
    private async Task<BackgroundEmailRequestResult> ProcessEmailChangeAsync(Guid accountId, string newEmail = NewEmail)
    {
        return await CreateProcessingService().ProcessAsync(new BackgroundEmailRequest.EmailChange
        {
            AccountId = accountId,
            NewEmailAddress = newEmail,
            IpAddress = IpAddress
        });
    }

    // Les liens de changement d'adresse gardés dans la fausse base pour ce compte.
    private List<EmailToken> FindEmailChangeLinksOf(User account)
    {
        return accountRepository.SavedEmailTokens.Where(token => token.UserId == account.Id && token.Type == EmailTokenType.EmailChange).ToList();
    }

    // Une adresse libre reçoit le lien qui la confirme, valable 24 heures. Seule l'empreinte du jeton est gardée (SS §2).
    [Fact]
    public async Task FreeAddressReceivesTheLinkThatConfirmsIt()
    {
        var account = AddAccount(AccountEmail);

        await ProcessEmailChangeAsync(account.Id);

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(NewEmail, email.RecipientAddress);
        Assert.Contains("https://walletflow.test/confirm-email-change#token=", email.Body);
        var link = Assert.Single(FindEmailChangeLinksOf(account));
        Assert.Equal(NewEmail, link.NewEmail);
        Assert.Equal(tokenGenerator.HashToken(ReadConfirmationToken(email.Body)), link.TokenHash);
        Assert.Equal(clock.GetUtcNow().UtcDateTime + EmailTokenValidityDurations.EmailChangeLink, link.ExpiresAt);
        Assert.Equal(AccountEmail, account.Email);
    }

    // SS §2 : une adresse déjà utilisée ne reçoit aucun lien ; son propriétaire est prévenu.
    [Fact]
    public async Task TakenAddressReceivesAWarningAndNoLink()
    {
        var account = AddAccount(AccountEmail);
        AddAccount(NewEmail);

        await ProcessEmailChangeAsync(account.Id);

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(NewEmail, email.RecipientAddress);
        Assert.Equal(changeEmails.CreateAddressAlreadyUsedEmail(NewEmail).Subject, email.Subject);
        Assert.DoesNotContain("#token=", email.Body);
        Assert.Empty(FindEmailChangeLinksOf(account));
    }

    // GR Compte : un nouveau lien remplace l'ancien.
    [Fact]
    public async Task NewLinkReplacesThePreviousOne()
    {
        var account = AddAccount(AccountEmail);

        await ProcessEmailChangeAsync(account.Id, "premiere@exemple.test");
        await ProcessEmailChangeAsync(account.Id, NewEmail);

        var link = Assert.Single(FindEmailChangeLinksOf(account));
        Assert.Equal(NewEmail, link.NewEmail);
    }

    // Le compte a été effacé depuis sa demande : rien ne part, et aucun lien n'est créé.
    [Fact]
    public async Task ErasedAccountReceivesNothing()
    {
        await ProcessEmailChangeAsync(Guid.NewGuid());

        Assert.Empty(emailSender.SentEmails);
        Assert.Empty(accountRepository.SavedEmailTokens);
    }

    // SS §9 : l'ancienne adresse est prévenue, sans que l'e-mail cite la nouvelle.
    [Fact]
    public async Task OldAddressIsWarnedWithoutTheNewOne()
    {
        var account = AddAccount(NewEmail);

        await CreateProcessingService().ProcessAsync(new BackgroundEmailRequest.EmailChangedNotice
        {
            AccountId = account.Id,
            OldEmailAddress = AccountEmail,
            IpAddress = IpAddress
        });

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(AccountEmail, email.RecipientAddress);
        Assert.DoesNotContain(NewEmail, email.Body);
        Assert.Contains("support", email.Body);
    }

    // La panne d'envoi ne va qu'au journal technique : l'utilisateur a déjà reçu sa réponse, la même pour toutes les adresses.
    [Fact]
    public async Task SendingFailureIsGivenToTheTechnicalLog()
    {
        var account = AddAccount(AccountEmail);
        emailSender.AlwaysFails = true;

        var result = await ProcessEmailChangeAsync(account.Id);

        Assert.True(result.EmailCouldNotBeSent);
        Assert.Equal(account.Id, result.UserId);
    }
}
