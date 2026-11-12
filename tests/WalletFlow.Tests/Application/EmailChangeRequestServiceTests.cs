using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.AccountSettings;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie la demande de changement d'adresse (GR Compte, SS §2 et §6), avec de faux outils.
public class EmailChangeRequestServiceTests
{
    private const string NewEmail = "nouvelle@exemple.test";

    private readonly TwoFactorServicesWithFakeTools tools = new TwoFactorServicesWithFakeTools();
    private readonly FakeEmailChangeRequestLimiter requestLimiter = new FakeEmailChangeRequestLimiter();

    // Demande le changement, avec ce mot de passe, cette nouvelle adresse et ce code.
    private async Task<EmailChangeRequestResult> RequestAsync(Guid userId, string password, string newEmail = NewEmail, string? code = null)
    {
        return await AccountSettingsServices.CreateEmailChangeRequest(tools, requestLimiter).RequestAsync(new EmailChangeAttempt
        {
            UserId = userId,
            Password = password,
            NewEmail = newEmail,
            Code = code,
            IpAddress = TwoFactorServicesWithFakeTools.IpAddress
        });
    }

    // L'adresse ne change qu'à la confirmation : la demande confie seulement les e-mails à la file.
    [Fact]
    public async Task RightPasswordQueuesTheEmailsAndChangesNothingYet()
    {
        var account = tools.AddAccountWithoutTwoFactor();

        var result = await RequestAsync(account.Id, TwoFactorServicesWithFakeTools.AccountPassword, "  Nouvelle@Exemple.TEST ");

        Assert.Equal(EmailChangeRequestResult.RequestAccepted, result);
        Assert.Equal(TwoFactorServicesWithFakeTools.AccountEmail, account.Email);
        var request = Assert.IsType<BackgroundEmailRequest.EmailChange>(Assert.Single(tools.EmailQueue.AddedRequests));
        Assert.Equal(account.Id, request.AccountId);
        Assert.Equal(NewEmail, request.NewEmailAddress);
        Assert.Equal(TwoFactorServicesWithFakeTools.IpAddress, request.IpAddress);
        Assert.Equal(new List<string> { NewEmail }, requestLimiter.CountedAddresses);
    }

    // SS §2 : la réponse est la même, que la nouvelle adresse soit libre ou déjà utilisée. Seul l'e-mail préparé ensuite change.
    [Fact]
    public async Task TakenAddressGetsTheSameAnswerAsAFreeOne()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        tools.AddAccountWithoutTwoFactor("deja-prise@exemple.test");

        var freeAddressResult = await RequestAsync(account.Id, TwoFactorServicesWithFakeTools.AccountPassword, NewEmail);
        var takenAddressResult = await RequestAsync(account.Id, TwoFactorServicesWithFakeTools.AccountPassword, "deja-prise@exemple.test");

        Assert.Equal(EmailChangeRequestResult.RequestAccepted, freeAddressResult);
        Assert.Equal(EmailChangeRequestResult.RequestAccepted, takenAddressResult);
        Assert.Equal(2, tools.EmailQueue.AddedRequests.Count);
    }

    [Theory]
    [InlineData("adresse-sans-arobase")]
    [InlineData("")]
    [InlineData("a@b")]
    public async Task InvalidAddressIsRefusedBeforeAnyCheck(string newEmail)
    {
        var account = tools.AddAccountWithoutTwoFactor();

        var result = await RequestAsync(account.Id, TwoFactorServicesWithFakeTools.AccountPassword, newEmail);

        Assert.Equal(EmailChangeRequestResult.InvalidEmailAddress, result);
        Assert.Equal(0, tools.PasswordHasher.VerifiedPasswordCount);
        Assert.Empty(tools.EmailQueue.AddedRequests);
        Assert.Empty(requestLimiter.CountedAddresses);
    }

    // C'est déjà l'adresse du compte, tapée autrement : le refus est clair, il ne révèle rien.
    [Fact]
    public async Task CurrentAddressIsRefusedClearly()
    {
        var account = tools.AddAccountWithoutTwoFactor();

        var result = await RequestAsync(account.Id, TwoFactorServicesWithFakeTools.AccountPassword, " UTILISATEUR@exemple.test");

        Assert.Equal(EmailChangeRequestResult.SameEmailAddress, result);
        Assert.Empty(tools.EmailQueue.AddedRequests);
    }

    [Fact]
    public async Task WrongPasswordQueuesNothingCountsNothingAndIsJournaled()
    {
        var account = tools.AddAccountWithoutTwoFactor();

        var result = await RequestAsync(account.Id, TwoFactorServicesWithFakeTools.WrongPassword);

        Assert.Equal(EmailChangeRequestResult.WrongPassword, result);
        Assert.Empty(tools.EmailQueue.AddedRequests);
        Assert.Empty(requestLimiter.CountedAddresses);
        Assert.Equal(1, tools.CountSecurityEvents(SecurityEventType.EmailChange, SecurityEventResult.Failure));
    }

    // SS §6 : la limite par nouvelle adresse ne compte que les demandes du propriétaire du compte, une fois son mot de passe vérifié.
    [Fact]
    public async Task LimitOfTheNewAddressIsCheckedOnlyAfterThePassword()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        requestLimiter.RefusesEveryRequest = true;

        var wrongPasswordResult = await RequestAsync(account.Id, TwoFactorServicesWithFakeTools.WrongPassword);
        var rightPasswordResult = await RequestAsync(account.Id, TwoFactorServicesWithFakeTools.AccountPassword);

        Assert.Equal(EmailChangeRequestResult.WrongPassword, wrongPasswordResult);
        Assert.Equal(EmailChangeRequestResult.TooManyRequests, rightPasswordResult);
        Assert.Single(requestLimiter.CountedAddresses);
        Assert.Empty(tools.EmailQueue.AddedRequests);
    }

    [Fact]
    public async Task TwoFactorAccountNeedsTheCodeOfItsApplication()
    {
        var activatedAccount = tools.AddActivatedAccount();
        var accountId = activatedAccount.UserAccount.Id;

        var withoutCodeResult = await RequestAsync(accountId, TwoFactorServicesWithFakeTools.AccountPassword);
        var wrongCodeResult = await RequestAsync(accountId, TwoFactorServicesWithFakeTools.AccountPassword, code: tools.FindWrongCode(activatedAccount.Secret));
        var rightCodeResult = await RequestAsync(accountId, TwoFactorServicesWithFakeTools.AccountPassword, code: tools.CalculateCurrentCode(activatedAccount.Secret));

        Assert.Equal(EmailChangeRequestResult.WrongPasswordOrCode, withoutCodeResult);
        Assert.Equal(EmailChangeRequestResult.WrongPasswordOrCode, wrongCodeResult);
        Assert.Equal(EmailChangeRequestResult.RequestAccepted, rightCodeResult);
        Assert.Single(tools.EmailQueue.AddedRequests);
    }

    // Avec la double authentification, un mauvais mot de passe reçoit le même refus qu'un mauvais code.
    [Fact]
    public async Task TwoFactorAccountWithWrongPasswordGetsTheSharedRefusal()
    {
        var activatedAccount = tools.AddActivatedAccount();

        var result = await RequestAsync(
            activatedAccount.UserAccount.Id,
            TwoFactorServicesWithFakeTools.WrongPassword,
            code: tools.CalculateCurrentCode(activatedAccount.Secret));

        Assert.Equal(EmailChangeRequestResult.WrongPasswordOrCode, result);
    }

    // Le code de secours est marqué utilisé dès la demande : il ne peut pas servir deux fois.
    [Fact]
    public async Task BackupCodeServesOnlyOnce()
    {
        var activatedAccount = tools.AddActivatedAccount();
        var backupCode = activatedAccount.BackupCodes[0];

        var firstResult = await RequestAsync(activatedAccount.UserAccount.Id, TwoFactorServicesWithFakeTools.AccountPassword, code: backupCode);
        var secondResult = await RequestAsync(activatedAccount.UserAccount.Id, TwoFactorServicesWithFakeTools.AccountPassword, code: backupCode);

        Assert.Equal(EmailChangeRequestResult.RequestAccepted, firstResult);
        Assert.Equal(EmailChangeRequestResult.WrongPasswordOrCode, secondResult);
        Assert.Single(tools.TwoFactorRepository.SavedBackupCodes, savedCode => savedCode.UsedAt != null);
        Assert.Single(tools.EmailQueue.AddedRequests);
    }
}
