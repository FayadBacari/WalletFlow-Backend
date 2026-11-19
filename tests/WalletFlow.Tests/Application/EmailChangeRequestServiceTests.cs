using WalletFlow.Application.AccountSettings;
using WalletFlow.Domain.NotificationsAndSupport;
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

        Assert.IsType<EmailChangeRequestResult.RequestAccepted>(result);
        Assert.Equal(TwoFactorServicesWithFakeTools.AccountEmail, account.Email);
        var request = Assert.Single(tools.EmailRequests.SavedRequests);
        Assert.Equal(EmailRequestType.EmailChange, request.Type);
        Assert.Equal(account.Id, request.UserId);
        Assert.Equal(NewEmail, request.EmailAddress);
        Assert.Equal(TwoFactorServicesWithFakeTools.IpAddress, request.IpAddress);
        Assert.Equal(tools.Clock.GetUtcNow().UtcDateTime, request.NextTryAt);
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

        Assert.IsType<EmailChangeRequestResult.RequestAccepted>(freeAddressResult);
        Assert.IsType<EmailChangeRequestResult.RequestAccepted>(takenAddressResult);
        Assert.Equal(2, tools.EmailRequests.SavedRequests.Count);
    }

    [Theory]
    [InlineData("adresse-sans-arobase")]
    [InlineData("")]
    [InlineData("a@b")]
    public async Task InvalidAddressIsRefusedBeforeAnyCheck(string newEmail)
    {
        var account = tools.AddAccountWithoutTwoFactor();

        var result = await RequestAsync(account.Id, TwoFactorServicesWithFakeTools.AccountPassword, newEmail);

        Assert.IsType<EmailChangeRequestResult.InvalidEmailAddress>(result);
        Assert.Equal(0, tools.PasswordHasher.VerifiedPasswordCount);
        Assert.Empty(tools.EmailRequests.SavedRequests);
        Assert.Empty(requestLimiter.CountedAddresses);
    }

    // C'est déjà l'adresse du compte, tapée autrement : le refus est clair, il ne révèle rien.
    [Fact]
    public async Task CurrentAddressIsRefusedClearly()
    {
        var account = tools.AddAccountWithoutTwoFactor();

        var result = await RequestAsync(account.Id, TwoFactorServicesWithFakeTools.AccountPassword, " UTILISATEUR@exemple.test");

        Assert.IsType<EmailChangeRequestResult.SameEmailAddress>(result);
        Assert.Empty(tools.EmailRequests.SavedRequests);
    }

    [Fact]
    public async Task WrongPasswordQueuesNothingCountsNothingAndIsJournaled()
    {
        var account = tools.AddAccountWithoutTwoFactor();

        var result = await RequestAsync(account.Id, TwoFactorServicesWithFakeTools.WrongPassword);

        Assert.IsType<EmailChangeRequestResult.WrongPassword>(result);
        Assert.Empty(tools.EmailRequests.SavedRequests);
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

        Assert.IsType<EmailChangeRequestResult.WrongPassword>(wrongPasswordResult);
        Assert.IsType<EmailChangeRequestResult.TooManyRequests>(rightPasswordResult);
        Assert.Single(requestLimiter.CountedAddresses);
        Assert.Empty(tools.EmailRequests.SavedRequests);
    }

    [Fact]
    public async Task TwoFactorAccountNeedsTheCodeOfItsApplication()
    {
        var activatedAccount = tools.AddActivatedAccount();
        var accountId = activatedAccount.UserAccount.Id;

        var withoutCodeResult = await RequestAsync(accountId, TwoFactorServicesWithFakeTools.AccountPassword);
        var wrongCodeResult = await RequestAsync(accountId, TwoFactorServicesWithFakeTools.AccountPassword, code: tools.FindWrongCode(activatedAccount.Secret));
        var rightCodeResult = await RequestAsync(accountId, TwoFactorServicesWithFakeTools.AccountPassword, code: tools.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<EmailChangeRequestResult.WrongPasswordOrCode>(withoutCodeResult);
        Assert.IsType<EmailChangeRequestResult.WrongPasswordOrCode>(wrongCodeResult);
        Assert.IsType<EmailChangeRequestResult.RequestAccepted>(rightCodeResult);
        Assert.Single(tools.EmailRequests.SavedRequests);
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

        Assert.IsType<EmailChangeRequestResult.WrongPasswordOrCode>(result);
    }

    // Le code de secours est marqué utilisé dès la demande : il ne peut pas servir deux fois.
    [Fact]
    public async Task BackupCodeServesOnlyOnce()
    {
        var activatedAccount = tools.AddActivatedAccount();
        var backupCode = activatedAccount.BackupCodes[0];

        var firstResult = await RequestAsync(activatedAccount.UserAccount.Id, TwoFactorServicesWithFakeTools.AccountPassword, code: backupCode);
        var secondResult = await RequestAsync(activatedAccount.UserAccount.Id, TwoFactorServicesWithFakeTools.AccountPassword, code: backupCode);

        Assert.IsType<EmailChangeRequestResult.RequestAccepted>(firstResult);
        Assert.IsType<EmailChangeRequestResult.WrongPasswordOrCode>(secondResult);
        Assert.Single(tools.TwoFactorRepository.SavedBackupCodes, savedCode => savedCode.UsedAt != null);

        // GR Notif : la demande et l'alerte du code de secours partent ensemble, une seule fois.
        Assert.Equal(
            new List<EmailRequestType> { EmailRequestType.EmailChange, EmailRequestType.BackupCodeUse },
            tools.EmailRequests.SavedRequests.Select(request => request.Type).Order().ToList());
    }

    // Le code de secours et la demande partent ensemble : si la demande ne peut pas entrer dans la file, le code reste valable.
    [Fact]
    public async Task BackupCodeIsNotSpentWhenTheRequestCannotBeQueued()
    {
        var activatedAccount = tools.AddActivatedAccount();
        tools.EmailRequests.IsDown = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => RequestAsync(
            activatedAccount.UserAccount.Id,
            TwoFactorServicesWithFakeTools.AccountPassword,
            code: activatedAccount.BackupCodes[0]));

        Assert.DoesNotContain(tools.TwoFactorRepository.SavedBackupCodes, savedCode => savedCode.UsedAt != null);
        Assert.Empty(tools.EmailRequests.SavedRequests);
    }

    // SS §2 : le code de l'application qui autorise la demande est noté avec elle : il ne resservira plus.
    [Fact]
    public async Task AuthenticatorCodeIsNotedWithTheRequest()
    {
        var activatedAccount = tools.AddActivatedAccount();

        var result = await RequestAsync(activatedAccount.UserAccount.Id, TwoFactorServicesWithFakeTools.AccountPassword, code: tools.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<EmailChangeRequestResult.RequestAccepted>(result);
        Assert.Equal(tools.CurrentStep(), activatedAccount.UserAccount.LastAuthenticatorStep);
    }

    // Une autre requête se sert du même code juste avant l'enregistrement : la demande n'entre pas dans la file (SS §2).
    [Fact]
    public async Task CodeUsedByAnotherRequestMeanwhileRefusesTheRequest()
    {
        var activatedAccount = tools.AddActivatedAccount();
        tools.AccountRepository.AuthenticatorStepUsedBeforeSave = tools.CurrentStep();

        var result = await RequestAsync(activatedAccount.UserAccount.Id, TwoFactorServicesWithFakeTools.AccountPassword, code: tools.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<EmailChangeRequestResult.WrongPasswordOrCode>(result);
        Assert.Empty(tools.EmailRequests.SavedRequests);
        Assert.Null(tools.AccountRepository.AuthenticatorStepUsedBeforeSave);
    }
}
