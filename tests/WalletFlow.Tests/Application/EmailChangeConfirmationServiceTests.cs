using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.AccountSettings;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie la confirmation d'une nouvelle adresse (GR Compte, SS §2), avec de faux outils.
public class EmailChangeConfirmationServiceTests
{
    private const string NewEmail = "nouvelle@exemple.test";

    private readonly TwoFactorServicesWithFakeTools tools = new TwoFactorServicesWithFakeTools();

    // Ajoute à la fausse base un lien de ce type pour ce compte, valable encore 24 heures, et renvoie le jeton écrit dans le lien.
    private string AddLink(User account, EmailTokenType linkType, string? newEmail)
    {
        var linkToken = tools.TokenGenerator.GenerateToken();
        tools.AccountRepository.SavedEmailTokens.Add(new EmailToken
        {
            Type = linkType,
            TokenHash = linkToken.Hash,
            ExpiresAt = tools.Clock.GetUtcNow().UtcDateTime + EmailTokenValidityDurations.EmailChangeLink,
            NewEmail = newEmail,
            UserId = account.Id
        });

        return linkToken.Token;
    }

    // Ajoute un lien de changement d'adresse vers la nouvelle adresse des tests.
    private string AddEmailChangeLink(User account)
    {
        return AddLink(account, EmailTokenType.EmailChange, NewEmail);
    }

    // Confirme avec ce jeton et ce mot de passe.
    private async Task<EmailChangeConfirmationResult> ConfirmAsync(string linkToken, string password)
    {
        return await AccountSettingsServices.CreateEmailChangeConfirmation(tools).ConfirmAsync(
            linkToken,
            password,
            TwoFactorServicesWithFakeTools.IpAddress);
    }

    // Le lien de changement d'adresse du compte gardé dans la fausse base.
    private EmailToken FindEmailChangeLinkOf(User account)
    {
        return tools.AccountRepository.SavedEmailTokens.Single(token => token.UserId == account.Id && token.Type == EmailTokenType.EmailChange);
    }

    // SS §2 et §9 : l'adresse change, le lien sert une fois, les liens partis vers l'ancienne adresse ne marchent plus,
    // et l'ancienne adresse est prévenue.
    [Fact]
    public async Task RightPasswordChangesTheAddressUsesTheLinkAndWarnsTheOldAddress()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        var otherAccount = tools.AddAccountWithoutTwoFactor("autre@exemple.test");
        var linkToken = AddEmailChangeLink(account);
        AddLink(account, EmailTokenType.PasswordRecovery, null);
        AddLink(account, EmailTokenType.AccountUnlock, null);
        AddLink(otherAccount, EmailTokenType.PasswordRecovery, null);

        var result = await ConfirmAsync(linkToken, TwoFactorServicesWithFakeTools.AccountPassword);

        Assert.IsType<EmailChangeConfirmationResult.EmailChanged>(result);
        Assert.Equal(NewEmail, account.Email);
        Assert.NotNull(FindEmailChangeLinkOf(account).UsedAt);
        Assert.Equal(new List<EmailTokenType> { EmailTokenType.EmailChange }, tools.AccountRepository.SavedEmailTokens.Where(token => token.UserId == account.Id).Select(token => token.Type).ToList());
        Assert.Single(tools.AccountRepository.SavedEmailTokens, token => token.UserId == otherAccount.Id);
        Assert.Equal(1, tools.CountSecurityEvents(SecurityEventType.EmailChange, SecurityEventResult.Success));
        var notice = Assert.IsType<BackgroundEmailRequest.EmailChangedNotice>(Assert.Single(tools.EmailQueue.AddedRequests));
        Assert.Equal(TwoFactorServicesWithFakeTools.AccountEmail, notice.OldEmailAddress);
        Assert.Equal(account.Id, notice.AccountId);
    }

    // Un mauvais mot de passe ne consomme pas le lien : l'adresse ne change pas, et l'ancienne n'est pas prévenue.
    [Fact]
    public async Task WrongPasswordKeepsTheLinkAndTheAddress()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        var linkToken = AddEmailChangeLink(account);

        var result = await ConfirmAsync(linkToken, TwoFactorServicesWithFakeTools.WrongPassword);

        Assert.IsType<EmailChangeConfirmationResult.WrongPassword>(result);
        Assert.Equal(TwoFactorServicesWithFakeTools.AccountEmail, account.Email);
        Assert.Null(FindEmailChangeLinkOf(account).UsedAt);
        Assert.Empty(tools.EmailQueue.AddedRequests);
        Assert.Equal(1, tools.CountSecurityEvents(SecurityEventType.EmailChange, SecurityEventResult.Failure));
    }

    [Fact]
    public async Task UnknownLinkIsRefused()
    {
        tools.AddAccountWithoutTwoFactor();

        var result = await ConfirmAsync("jeton-inconnu", TwoFactorServicesWithFakeTools.AccountPassword);

        Assert.IsType<EmailChangeConfirmationResult.LinkNoLongerValid>(result);
    }

    // SS §2 : le lien vaut 24 heures, pas une seconde de plus.
    [Fact]
    public async Task LinkExpiresAfterTwentyFourHours()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        var linkToken = AddEmailChangeLink(account);
        tools.Clock.Advance(EmailTokenValidityDurations.EmailChangeLink);

        var result = await ConfirmAsync(linkToken, TwoFactorServicesWithFakeTools.AccountPassword);

        Assert.IsType<EmailChangeConfirmationResult.LinkNoLongerValid>(result);
        Assert.Equal(TwoFactorServicesWithFakeTools.AccountEmail, account.Email);
    }

    [Fact]
    public async Task LinkServesOnlyOnce()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        var linkToken = AddEmailChangeLink(account);

        await ConfirmAsync(linkToken, TwoFactorServicesWithFakeTools.AccountPassword);
        var secondResult = await ConfirmAsync(linkToken, TwoFactorServicesWithFakeTools.AccountPassword);

        Assert.IsType<EmailChangeConfirmationResult.LinkNoLongerValid>(secondResult);
        Assert.Equal(1, tools.CountSecurityEvents(SecurityEventType.EmailChange, SecurityEventResult.Success));
    }

    // Un lien de récupération ne sert jamais à changer d'adresse.
    [Fact]
    public async Task LinkOfAnotherTypeIsRefused()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        var recoveryLinkToken = AddLink(account, EmailTokenType.PasswordRecovery, null);

        var result = await ConfirmAsync(recoveryLinkToken, TwoFactorServicesWithFakeTools.AccountPassword);

        Assert.IsType<EmailChangeConfirmationResult.LinkNoLongerValid>(result);
        Assert.Equal(TwoFactorServicesWithFakeTools.AccountEmail, account.Email);
    }

    // Un autre compte a pris l'adresse depuis la demande : le premier qui confirme la garde.
    [Fact]
    public async Task AddressTakenSinceTheRequestIsRefusedAndNothingChanges()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        var linkToken = AddEmailChangeLink(account);
        tools.AddAccountWithoutTwoFactor(NewEmail);

        var result = await ConfirmAsync(linkToken, TwoFactorServicesWithFakeTools.AccountPassword);

        Assert.IsType<EmailChangeConfirmationResult.EmailAlreadyUsed>(result);
        Assert.Equal(TwoFactorServicesWithFakeTools.AccountEmail, account.Email);
        Assert.Null(FindEmailChangeLinkOf(account).UsedAt);
        Assert.Empty(tools.EmailQueue.AddedRequests);
    }

    // Un autre compte prend l'adresse au tout dernier moment : la base refuse le double, tout est relu, et le refus est clair.
    [Fact]
    public async Task AddressTakenAtTheLastMomentIsRefusedAndNothingChanges()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        var linkToken = AddEmailChangeLink(account);
        tools.AccountRepository.AccountTakingTheNewEmailBeforeSave = new User
        {
            Email = NewEmail,
            PasswordHash = "empreinte-de-l-autre-compte",
            IsEmailConfirmed = true
        };

        var result = await ConfirmAsync(linkToken, TwoFactorServicesWithFakeTools.AccountPassword);

        Assert.IsType<EmailChangeConfirmationResult.EmailAlreadyUsed>(result);
        Assert.Equal(TwoFactorServicesWithFakeTools.AccountEmail, account.Email);
        Assert.Null(FindEmailChangeLinkOf(account).UsedAt);
        Assert.Equal(0, tools.UnitOfWork.CommittedTransactionCount);
    }

    // Une récupération change le mot de passe pendant la vérification : le lien n'est plus valable, l'adresse ne change pas.
    [Fact]
    public async Task PasswordChangedMeanwhileRefusesTheLink()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        var linkToken = AddEmailChangeLink(account);
        tools.PasswordHasher.OnPasswordVerified = () =>
            EntityValues.WriteProperty(account, nameof(User.PasswordHash), "empreinte-de-la-recuperation");

        var result = await ConfirmAsync(linkToken, TwoFactorServicesWithFakeTools.AccountPassword);

        Assert.IsType<EmailChangeConfirmationResult.LinkNoLongerValid>(result);
        Assert.Equal(TwoFactorServicesWithFakeTools.AccountEmail, account.Email);
    }
}
