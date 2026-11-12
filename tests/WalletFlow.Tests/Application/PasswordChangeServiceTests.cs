using WalletFlow.Application.AccountSettings;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le changement de mot de passe (GR Compte, SS §2 et §3), avec de faux outils.
public class PasswordChangeServiceTests
{
    private const string NewPassword = "Nouveau-Mot-De-Passe-3";

    private readonly TwoFactorServicesWithFakeTools tools = new TwoFactorServicesWithFakeTools();

    // Demande le changement, avec ce mot de passe actuel et ce code.
    private async Task<PasswordChangeResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string? code = null,
        string newPassword = NewPassword)
    {
        return await AccountSettingsServices.CreatePasswordChange(tools).ChangeAsync(new PasswordChangeAttempt
        {
            UserId = userId,
            CurrentPassword = currentPassword,
            NewPassword = newPassword,
            Code = code,
            IpAddress = TwoFactorServicesWithFakeTools.IpAddress
        });
    }

    // Ajoute à la fausse base une session ouverte de ce compte.
    private UserSession AddOpenSession(User account)
    {
        var session = new UserSession
        {
            Id = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + new string('0', 32),
            LastActivityAt = tools.Clock.GetUtcNow().UtcDateTime,
            UserId = account.Id
        };
        tools.SessionRepository.SavedSessions.Add(session);

        return session;
    }

    // Les sessions de ce compte gardées dans la fausse base.
    private List<UserSession> FindSessionsOf(User account)
    {
        return tools.SessionRepository.SavedSessions.Where(session => session.UserId == account.Id).ToList();
    }

    // SS §3 : toutes les sessions se ferment, et la session en cours continue avec un nouvel identifiant.
    [Fact]
    public async Task RightPasswordChangesItClosesEverySessionAndGivesTheCurrentOneANewIdentifier()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        AddOpenSession(account);
        AddOpenSession(account);
        var otherAccountSession = AddOpenSession(tools.AddAccountWithoutTwoFactor("autre@exemple.test"));

        var result = await ChangePasswordAsync(account.Id, TwoFactorServicesWithFakeTools.AccountPassword);

        var changed = Assert.IsType<PasswordChangeResult.Changed>(result);
        Assert.Equal(FakePasswordHasher.HashPrefix + NewPassword, account.PasswordHash);
        var newSession = Assert.Single(FindSessionsOf(account));
        Assert.Equal(tools.TokenGenerator.HashToken(changed.SessionToken), newSession.Id);
        Assert.Contains(otherAccountSession, tools.SessionRepository.SavedSessions);
        Assert.Equal(1, tools.CountSecurityEvents(SecurityEventType.PasswordChange, SecurityEventResult.Success));
        Assert.Equal(1, tools.UnitOfWork.CommittedTransactionCount);
    }

    // Le nouveau mot de passe est vérifié avant tout : aucun mot de passe n'est vérifié ni haché.
    [Fact]
    public async Task NewPasswordBreakingTheRulesIsRefusedBeforeAnyCheck()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        var oldHash = account.PasswordHash;

        var result = await ChangePasswordAsync(account.Id, TwoFactorServicesWithFakeTools.AccountPassword, newPassword: "trop-court");

        Assert.IsType<PasswordChangeResult.NewPasswordBreaksRules>(result);
        Assert.Equal(oldHash, account.PasswordHash);
        Assert.Equal(0, tools.PasswordHasher.VerifiedPasswordCount);
        Assert.Equal(0, tools.PasswordHasher.HashedPasswordCount);
        Assert.Empty(tools.SecurityEventRepository.SavedSecurityEvents);
    }

    [Fact]
    public async Task WrongCurrentPasswordChangesNothingAndIsJournaled()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        var oldHash = account.PasswordHash;
        var session = AddOpenSession(account);

        var result = await ChangePasswordAsync(account.Id, TwoFactorServicesWithFakeTools.WrongPassword);

        Assert.IsType<PasswordChangeResult.WrongPassword>(result);
        Assert.Equal(oldHash, account.PasswordHash);
        Assert.Equal(session, Assert.Single(FindSessionsOf(account)));
        Assert.Equal(0, tools.PasswordHasher.HashedPasswordCount);
        Assert.Equal(1, tools.CountSecurityEvents(SecurityEventType.PasswordChange, SecurityEventResult.Failure));
        Assert.Equal(0, tools.UnitOfWork.CommittedTransactionCount);
    }

    // SS §2 : les mauvais mots de passe saisis ici ne bloquent pas le compte, sinon un voleur de session pourrait le bloquer.
    [Fact]
    public async Task WrongCurrentPasswordsNeverCountForTheLockout()
    {
        var account = tools.AddAccountWithoutTwoFactor();

        for (var attemptNumber = 1; attemptNumber <= AccountLockoutRules.MaximumFailedAttempts + 1; attemptNumber++)
        {
            await ChangePasswordAsync(account.Id, TwoFactorServicesWithFakeTools.WrongPassword);
        }

        Assert.Equal(0, account.FailedLoginAttemptCount);
        Assert.Null(account.LockoutEndsAt);
    }

    [Fact]
    public async Task SamePasswordAsTheCurrentOneIsAccepted()
    {
        var account = tools.AddAccountWithoutTwoFactor();

        var result = await ChangePasswordAsync(account.Id, TwoFactorServicesWithFakeTools.AccountPassword, newPassword: TwoFactorServicesWithFakeTools.AccountPassword);

        Assert.IsType<PasswordChangeResult.Changed>(result);
    }

    [Fact]
    public async Task TwoFactorAccountChangesItsPasswordWithTheCodeOfItsApplication()
    {
        var activatedAccount = tools.AddActivatedAccount();

        var result = await ChangePasswordAsync(
            activatedAccount.UserAccount.Id,
            TwoFactorServicesWithFakeTools.AccountPassword,
            tools.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<PasswordChangeResult.Changed>(result);
        Assert.Equal(FakePasswordHasher.HashPrefix + NewPassword, activatedAccount.UserAccount.PasswordHash);
    }

    // Avec la double authentification, un mauvais mot de passe, un code absent et un mauvais code reçoivent le même refus.
    [Theory]
    [InlineData(TwoFactorServicesWithFakeTools.WrongPassword, "bon-code")]
    [InlineData(TwoFactorServicesWithFakeTools.AccountPassword, null)]
    [InlineData(TwoFactorServicesWithFakeTools.AccountPassword, "")]
    [InlineData(TwoFactorServicesWithFakeTools.AccountPassword, "mauvais-code")]
    public async Task TwoFactorAccountGetsTheSameRefusalForAWrongPasswordAndAWrongCode(string currentPassword, string? codeKind)
    {
        var activatedAccount = tools.AddActivatedAccount();
        var oldHash = activatedAccount.UserAccount.PasswordHash;
        var code = codeKind;
        if (codeKind == "bon-code")
        {
            code = tools.CalculateCurrentCode(activatedAccount.Secret);
        }
        else if (codeKind == "mauvais-code")
        {
            code = tools.FindWrongCode(activatedAccount.Secret);
        }

        var result = await ChangePasswordAsync(activatedAccount.UserAccount.Id, currentPassword, code);

        Assert.IsType<PasswordChangeResult.WrongPasswordOrCode>(result);
        Assert.Equal(oldHash, activatedAccount.UserAccount.PasswordHash);
        Assert.Equal(1, tools.CountSecurityEvents(SecurityEventType.PasswordChange, SecurityEventResult.Failure));
    }

    // Un mauvais mot de passe ne consomme pas le code : le propriétaire peut encore s'en servir juste après.
    [Fact]
    public async Task WrongPasswordDoesNotUseTheCode()
    {
        var activatedAccount = tools.AddActivatedAccount();
        var code = tools.CalculateCurrentCode(activatedAccount.Secret);

        await ChangePasswordAsync(activatedAccount.UserAccount.Id, TwoFactorServicesWithFakeTools.WrongPassword, code);
        var result = await ChangePasswordAsync(activatedAccount.UserAccount.Id, TwoFactorServicesWithFakeTools.AccountPassword, code);

        Assert.IsType<PasswordChangeResult.Changed>(result);
    }

    [Fact]
    public async Task BackupCodeChangesThePasswordAndNeverServesAgain()
    {
        var activatedAccount = tools.AddActivatedAccount();
        var backupCode = activatedAccount.BackupCodes[0];

        var firstResult = await ChangePasswordAsync(activatedAccount.UserAccount.Id, TwoFactorServicesWithFakeTools.AccountPassword, backupCode);
        var secondResult = await ChangePasswordAsync(activatedAccount.UserAccount.Id, NewPassword, backupCode);

        Assert.IsType<PasswordChangeResult.Changed>(firstResult);
        Assert.IsType<PasswordChangeResult.WrongPasswordOrCode>(secondResult);
        Assert.Single(tools.TwoFactorRepository.SavedBackupCodes, savedCode => savedCode.UsedAt != null);
    }

    // Un code de l'application ne sert qu'une fois, même pour deux actions différentes (SS §2).
    [Fact]
    public async Task AuthenticatorCodeAlreadyUsedIsRefused()
    {
        var activatedAccount = tools.AddActivatedAccount();
        var code = tools.CalculateCurrentCode(activatedAccount.Secret);

        var firstResult = await ChangePasswordAsync(activatedAccount.UserAccount.Id, TwoFactorServicesWithFakeTools.AccountPassword, code);
        var secondResult = await ChangePasswordAsync(activatedAccount.UserAccount.Id, NewPassword, code);

        Assert.IsType<PasswordChangeResult.Changed>(firstResult);
        Assert.IsType<PasswordChangeResult.WrongPasswordOrCode>(secondResult);
    }

    // Une récupération du mot de passe passe pendant la vérification : ce changement ne doit pas écraser le mot de passe récupéré.
    [Fact]
    public async Task PasswordReplacedByARecoveryMeanwhileIsNeverOverwritten()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        var session = AddOpenSession(account);
        tools.PasswordHasher.OnPasswordVerified = () =>
            EntityValues.WriteProperty(account, nameof(User.PasswordHash), "empreinte-de-la-recuperation");

        var result = await ChangePasswordAsync(account.Id, TwoFactorServicesWithFakeTools.AccountPassword);

        Assert.IsType<PasswordChangeResult.WrongPassword>(result);
        Assert.Equal("empreinte-de-la-recuperation", account.PasswordHash);
        Assert.Equal(session, Assert.Single(FindSessionsOf(account)));
        Assert.Equal(0, tools.CountSecurityEvents(SecurityEventType.PasswordChange, SecurityEventResult.Success));
    }

    // D'autres requêtes changent le compte juste avant l'enregistrement : tout est relu, puis enregistré une seule fois.
    [Fact]
    public async Task ConflictsAreRetriedThenTheChangeIsSavedOnce()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        AddOpenSession(account);
        tools.SessionRepository.LoginConflicts = 2;

        var result = await ChangePasswordAsync(account.Id, TwoFactorServicesWithFakeTools.AccountPassword);

        var changed = Assert.IsType<PasswordChangeResult.Changed>(result);
        Assert.Equal(tools.TokenGenerator.HashToken(changed.SessionToken), Assert.Single(FindSessionsOf(account)).Id);
        Assert.Equal(1, tools.CountSecurityEvents(SecurityEventType.PasswordChange, SecurityEventResult.Success));
        Assert.Equal(1, tools.UnitOfWork.CommittedTransactionCount);
    }

    // Les autres requêtes ne laissent jamais passer l'enregistrement : le changement est refusé, jamais fait à moitié.
    [Fact]
    public async Task ChangeThatCanNeverBeSavedIsRefusedAndChangesNothing()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        var oldHash = account.PasswordHash;
        var session = AddOpenSession(account);
        tools.SessionRepository.LoginConflicts = 1000;

        var result = await ChangePasswordAsync(account.Id, TwoFactorServicesWithFakeTools.AccountPassword);

        Assert.IsType<PasswordChangeResult.WrongPassword>(result);
        Assert.Equal(oldHash, account.PasswordHash);
        Assert.Equal(session, Assert.Single(FindSessionsOf(account)));
        Assert.Equal(0, tools.UnitOfWork.CommittedTransactionCount);
    }
}
