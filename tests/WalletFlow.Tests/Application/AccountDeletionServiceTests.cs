using WalletFlow.Application.AccountDeletion;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Application.TwoFactorServicesWithFakeTools;

namespace WalletFlow.Tests.Application;

// Vérifie la suppression d'un compte avec de faux outils (GR Suppression, SS §2, §7 et §11) : la demande et tout ce qu'elle
// ferme, la révocation des banques, l'annulation par une connexion, et l'effacement 30 jours après.
public class AccountDeletionServiceTests
{
    private readonly TwoFactorServicesWithFakeTools tools = new TwoFactorServicesWithFakeTools();

    private DateTime Now => tools.Clock.GetUtcNow().UtcDateTime;

    // Demande la suppression, avec ce mot de passe et ce code.
    private async Task<AccountDeletionResult> RequestAsync(Guid userId, string password, string? code = null)
    {
        return await AccountSettingsServices.CreateAccountDeletion(tools).RequestAsync(new AccountDeletionAttempt
        {
            UserId = userId,
            Password = password,
            Code = code,
            IpAddress = IpAddress
        });
    }

    // Ajoute une connexion bancaire active à ce compte.
    private BankConnection AddConnection(Guid userId, string externalId)
    {
        var connection = new BankConnection
        {
            ExternalConnectionId = externalId,
            BankName = "Banque",
            AuthorizationExpiresAt = Now.AddDays(90),
            UserId = userId
        };
        tools.BankRepository.SavedConnections.Add(connection);

        return connection;
    }

    // Les événements du journal de ce type.
    private List<SecurityEvent> Events(SecurityEventType type)
    {
        return tools.SecurityEventRepository.SavedSecurityEvents.Where(securityEvent => securityEvent.Type == type).ToList();
    }

    // Le bon mot de passe demande la suppression : effacement dans 30 jours, toutes les sessions et tous les liens du compte
    // effacés, et seulement les siens, l'e-mail de confirmation, et la banque du compte révoquée puis marquée expirée.
    [Fact]
    public async Task RightPasswordRequestsTheDeletionClosesEverythingAndRevokesTheBank()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        var other = tools.AddAccountWithoutTwoFactor("autre@exemple.test");
        tools.SessionRepository.SavedSessions.Add(new UserSession { CreatedAt = Now, Id = "session-1", LastActivityAt = Now, UserId = account.Id });
        tools.SessionRepository.SavedSessions.Add(new UserSession { CreatedAt = Now, Id = "session-2", LastActivityAt = Now, UserId = account.Id });
        tools.SessionRepository.SavedSessions.Add(new UserSession { CreatedAt = Now, Id = "session-autre", LastActivityAt = Now, UserId = other.Id });
        tools.AccountRepository.SavedEmailTokens.Add(new EmailToken { Type = EmailTokenType.PasswordRecovery, TokenHash = "lien-1", ExpiresAt = Now.AddMinutes(30), UserId = account.Id });
        tools.AccountRepository.SavedEmailTokens.Add(new EmailToken
        {
            Type = EmailTokenType.EmailChange,
            TokenHash = "lien-2",
            NewEmail = "nouvelle@exemple.test",
            ExpiresAt = Now.AddHours(24),
            UserId = account.Id
        });
        tools.AccountRepository.SavedEmailTokens.Add(new EmailToken { Type = EmailTokenType.PasswordRecovery, TokenHash = "lien-autre", ExpiresAt = Now.AddMinutes(30), UserId = other.Id });
        var connection = AddConnection(account.Id, "session-banque");
        var otherConnection = AddConnection(other.Id, "session-banque-autre");

        var result = await RequestAsync(account.Id, AccountPassword);

        Assert.Equal(Now.AddDays(30), Assert.IsType<AccountDeletionResult.Requested>(result).ErasesAt);
        Assert.Equal(Now, account.DeletionRequestedAt);
        Assert.Equal("session-autre", Assert.Single(tools.SessionRepository.SavedSessions).Id);
        Assert.Equal("lien-autre", Assert.Single(tools.AccountRepository.SavedEmailTokens).TokenHash);
        var email = Assert.Single(tools.EmailRequests.SavedRequests);
        Assert.Equal((EmailRequestType.DeletionRequest, (Guid?)account.Id, IpAddress), (email.Type, email.UserId, email.IpAddress));
        Assert.Equal(new List<string> { "session-banque" }, tools.BankProvider.RevokedSessions);
        Assert.Equal((BankConnectionStatus.Expired, BankConnectionStatus.Active), (connection.Status, otherConnection.Status));
        Assert.Single(Events(SecurityEventType.DeletionRequest), securityEvent =>
            securityEvent.Result == SecurityEventResult.Success && securityEvent.UserId == account.Id && Equals(securityEvent.IpAddress, IpAddress));
        Assert.Single(Events(SecurityEventType.BankDisconnection), securityEvent =>
            securityEvent.UserId == account.Id && Equals(securityEvent.IpAddress, IpAddress));
    }

    // Un secret de l'application changé pendant la vérification arrête la demande : le code de secours a été vérifié pour une
    // autre activation de la double authentification. Il reste valable, et rien n'est demandé.
    [Fact]
    public async Task AuthenticatorSecretChangedMeanwhileStopsTheRequest()
    {
        var activatedAccount = tools.AddActivatedAccount();
        tools.PasswordHasher.OnPasswordVerified = () => activatedAccount.UserAccount.ActivateTwoFactor("autre-secret-chiffre", TwoFactorTestTools.OldStep);

        var result = await RequestAsync(activatedAccount.UserAccount.Id, AccountPassword, activatedAccount.BackupCodes[0]);

        Assert.IsType<AccountDeletionResult.WrongPasswordOrCode>(result);
        Assert.Null(activatedAccount.UserAccount.DeletionRequestedAt);
        Assert.DoesNotContain(tools.TwoFactorRepository.SavedBackupCodes, savedCode => savedCode.UsedAt != null);
        Assert.Empty(tools.EmailRequests.SavedRequests);
    }

    // Un mauvais mot de passe ne change rien, et le refus est noté au journal.
    [Fact]
    public async Task WrongPasswordChangesNothingAndIsJournaled()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        tools.SessionRepository.SavedSessions.Add(new UserSession { CreatedAt = Now, Id = "session-1", LastActivityAt = Now, UserId = account.Id });
        var connection = AddConnection(account.Id, "session-banque");

        var result = await RequestAsync(account.Id, WrongPassword);

        Assert.IsType<AccountDeletionResult.WrongPassword>(result);
        Assert.Null(account.DeletionRequestedAt);
        Assert.Single(tools.SessionRepository.SavedSessions);
        Assert.Empty(tools.EmailRequests.SavedRequests);
        Assert.Empty(tools.BankProvider.RevokedSessions);
        Assert.Equal(BankConnectionStatus.Active, connection.Status);
        Assert.Single(Events(SecurityEventType.DeletionRequest), securityEvent => securityEvent.Result == SecurityEventResult.Failure);
    }

    // Avec la double authentification, il faut aussi le code ; sans lui, le refus ne dit pas lequel des deux est faux.
    [Fact]
    public async Task TwoFactorAccountNeedsTheCodeOfItsApplication()
    {
        var activatedAccount = tools.AddActivatedAccount();
        var accountId = activatedAccount.UserAccount.Id;

        var withoutCode = await RequestAsync(accountId, AccountPassword);
        var rightCode = await RequestAsync(accountId, AccountPassword, tools.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<AccountDeletionResult.WrongPasswordOrCode>(withoutCode);
        Assert.IsType<AccountDeletionResult.Requested>(rightCode);
        Assert.NotNull(activatedAccount.UserAccount.DeletionRequestedAt);
    }

    // Un code de secours sert une fois, avec son alerte, dans la même transaction que la demande.
    [Fact]
    public async Task BackupCodeIsSpentWithTheRequestAndSendsItsAlert()
    {
        var activatedAccount = tools.AddActivatedAccount();

        var result = await RequestAsync(activatedAccount.UserAccount.Id, AccountPassword, activatedAccount.BackupCodes[0]);

        Assert.IsType<AccountDeletionResult.Requested>(result);
        Assert.Single(tools.TwoFactorRepository.SavedBackupCodes, savedCode => savedCode.UsedAt != null);
        Assert.Equal(
            new List<EmailRequestType> { EmailRequestType.BackupCodeUse, EmailRequestType.DeletionRequest },
            tools.EmailRequests.SavedRequests.Select(request => request.Type).Order().ToList());
    }

    // SS §7 : si le prestataire ne répond pas, la demande est acceptée quand même ; la connexion reste à révoquer, et la tâche de
    // fond la révoque à son passage suivant.
    [Fact]
    public async Task ProviderDownLeavesTheRevocationToTheBackgroundTask()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        var connection = AddConnection(account.Id, "session-banque");
        tools.BankProvider.RevocationFails = true;

        var result = await RequestAsync(account.Id, AccountPassword);
        var revocation = AccountSettingsServices.CreateBankRevocation(tools);
        var toRevokeWhileDown = await revocation.FindConnectionsToRevokeAsync();
        tools.BankProvider.RevocationFails = false;
        var revokedLater = await revocation.TryRevokeAsync(connection.Id, null);

        Assert.IsType<AccountDeletionResult.Requested>(result);
        Assert.Equal(new List<Guid> { connection.Id }, toRevokeWhileDown);
        Assert.True(revokedLater);
        Assert.Equal(BankConnectionStatus.Expired, connection.Status);
        Assert.Empty(await revocation.FindConnectionsToRevokeAsync());
    }

    // Une session renouvelée chez le prestataire juste avant que la connexion soit marquée expirée n'est pas révoquée : la
    // connexion n'est pas marquée, et reste à révoquer.
    [Fact]
    public async Task ConnectionRenewedMeanwhileStaysToRevoke()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        account.RequestDeletion(Now);
        var connection = AddConnection(account.Id, "session-banque");
        tools.BankRepository.OtherRequestBeforeCheckedSave = renewed => renewed.Renew("nouvelle-session", Now.AddDays(180));
        var revocation = AccountSettingsServices.CreateBankRevocation(tools);

        var revoked = await revocation.TryRevokeAsync(connection.Id, null);

        Assert.False(revoked);
        Assert.Equal(("nouvelle-session", BankConnectionStatus.Active), (connection.ExternalConnectionId, connection.Status));
        Assert.Equal(new List<Guid> { connection.Id }, await revocation.FindConnectionsToRevokeAsync());
    }

    // Une connexion qui annule la suppression pendant le passage de la tâche de fond arrête la révocation qui restait à faire :
    // l'utilisateur garde sa banque, sans autorisation à renouveler.
    [Fact]
    public async Task CancelledDeletionKeepsTheBankStillToRevoke()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        account.RequestDeletion(Now);
        var connection = AddConnection(account.Id, "session-banque");
        var revocation = AccountSettingsServices.CreateBankRevocation(tools);
        var toRevoke = await revocation.FindConnectionsToRevokeAsync();
        account.CancelDeletion();

        var revoked = await revocation.TryRevokeAsync(connection.Id, null);

        Assert.Equal(new List<Guid> { connection.Id }, toRevoke);
        Assert.False(revoked);
        Assert.Empty(tools.BankProvider.RevokedSessions);
        Assert.Equal(BankConnectionStatus.Active, connection.Status);
    }

    // Une deuxième demande, d'un autre onglet au même instant, ne repousse pas l'effacement et n'envoie rien de plus.
    [Fact]
    public async Task SecondRequestKeepsTheFirstErasureDate()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        account.RequestDeletion(Now.AddDays(-5));

        var result = await RequestAsync(account.Id, AccountPassword);

        Assert.Equal(Now.AddDays(25), Assert.IsType<AccountDeletionResult.Requested>(result).ErasesAt);
        Assert.Equal(Now.AddDays(-5), account.DeletionRequestedAt);
        Assert.Empty(tools.EmailRequests.SavedRequests);
        Assert.DoesNotContain(Events(SecurityEventType.DeletionRequest), securityEvent => securityEvent.Result == SecurityEventResult.Success);
    }

    // Un mot de passe changé juste avant l'enregistrement, par une récupération au même instant, arrête la demande : le compte
    // contrôlé fait recommencer le passage, qui voit le nouveau mot de passe.
    [Fact]
    public async Task PasswordChangedJustBeforeTheSaveStopsTheRequest()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        tools.AccountRepository.PasswordChangedBeforeCheckedSave = "empreinte-changee-entre-temps";

        var result = await RequestAsync(account.Id, AccountPassword);

        Assert.IsType<AccountDeletionResult.WrongPassword>(result);
        Assert.Null(account.DeletionRequestedAt);
        Assert.Empty(tools.EmailRequests.SavedRequests);
    }

    // GR Suppression : une connexion pendant les 30 jours annule la demande, et l'alerte part. La connexion suivante n'annule rien.
    [Fact]
    public async Task LoginDuringTheThirtyDaysCancelsTheDeletion()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        await RequestAsync(account.Id, AccountPassword);
        tools.EmailRequests.SavedRequests.Clear();

        var first = await tools.LogInAsync(AccountPassword);
        var second = await tools.LogInAsync(AccountPassword);

        Assert.True(Assert.IsType<AccountLoginResult.LoggedIn>(first).HasCancelledDeletion);
        Assert.False(Assert.IsType<AccountLoginResult.LoggedIn>(second).HasCancelledDeletion);
        Assert.Null(account.DeletionRequestedAt);
        var alert = Assert.Single(tools.EmailRequests.SavedRequests, request => request.Type == EmailRequestType.DeletionCancellation);
        Assert.Equal((Guid?)account.Id, alert.UserId);
    }

    // Le compte n'est effacé qu'au passage de la tâche horaire qui suit les 30 jours : une connexion juste avant ce passage annule
    // encore la demande, et le passage ne trouve alors plus rien à effacer.
    [Fact]
    public async Task LoginAfterTheThirtyDaysButBeforeTheErasureStillCancels()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        account.RequestDeletion(Now.AddDays(-30).AddMinutes(-30));
        var erasure = AccountSettingsServices.CreateAccountErasure(tools);
        var dueBeforeTheLogin = await erasure.FindAccountsToEraseAsync();

        var login = await tools.LogInAsync(AccountPassword);
        var erased = await erasure.EraseAsync(account.Id);

        Assert.Equal(new List<Guid> { account.Id }, dueBeforeTheLogin);
        Assert.True(Assert.IsType<AccountLoginResult.LoggedIn>(login).HasCancelledDeletion);
        Assert.False(erased);
        Assert.Contains(account, tools.AccountRepository.SavedUsers);
    }

    // Avec la double authentification, c'est la connexion finie avec le code qui annule la demande.
    [Fact]
    public async Task TwoFactorLoginCancelsTheDeletionOnceTheCodeIsRight()
    {
        var activatedAccount = tools.AddActivatedAccount();
        activatedAccount.UserAccount.RequestDeletion(Now);

        var waitingLoginToken = await tools.LogInThenGetWaitingLoginTokenAsync();
        Assert.NotNull(activatedAccount.UserAccount.DeletionRequestedAt);
        var result = await tools.FinishLoginAsync(waitingLoginToken, tools.CalculateCurrentCode(activatedAccount.Secret));

        Assert.True(Assert.IsType<TwoFactorLoginResult.LoggedIn>(result).HasCancelledDeletion);
        Assert.Null(activatedAccount.UserAccount.DeletionRequestedAt);
    }

    // Le compte est effacé 30 jours pile après la demande, pas avant, avec l'e-mail qui le confirme : il ne vise plus de compte,
    // et garde l'adresse.
    [Fact]
    public async Task AccountIsErasedThirtyDaysAfterTheRequestWithItsEmail()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        account.RequestDeletion(Now);
        var erasure = AccountSettingsServices.CreateAccountErasure(tools);

        tools.Clock.Advance(TimeSpan.FromDays(30) - TimeSpan.FromTicks(1));
        var dueTooEarly = await erasure.FindAccountsToEraseAsync();
        var erasedTooEarly = await erasure.EraseAsync(account.Id);
        tools.Clock.Advance(TimeSpan.FromTicks(1));
        var due = await erasure.FindAccountsToEraseAsync();
        var erased = await erasure.EraseAsync(account.Id);

        Assert.Empty(dueTooEarly);
        Assert.False(erasedTooEarly);
        Assert.Equal(new List<Guid> { account.Id }, due);
        Assert.True(erased);
        Assert.DoesNotContain(account, tools.AccountRepository.SavedUsers);
        var email = Assert.Single(tools.EmailRequests.SavedRequests);
        Assert.Equal((EmailRequestType.AccountErasure, (Guid?)null, (string?)AccountEmail), (email.Type, email.UserId, email.EmailAddress));
    }

    // Une connexion qui annule la demande au même instant que l'effacement le fait échouer : le passage suivant relit le compte,
    // qui n'est plus à effacer. Rien ne part.
    [Fact]
    public async Task LoginAtTheSameInstantKeepsTheAccount()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        account.RequestDeletion(Now.AddDays(-30));
        tools.AccountRepository.DeletionCancelledBeforeCheckedSave = true;

        var erased = await AccountSettingsServices.CreateAccountErasure(tools).EraseAsync(account.Id);

        Assert.False(erased);
        Assert.Contains(account, tools.AccountRepository.SavedUsers);
        Assert.Null(account.DeletionRequestedAt);
        Assert.Empty(tools.EmailRequests.SavedRequests);
    }

    // Un compte sans demande de suppression, ou inconnu, n'est jamais effacé.
    [Fact]
    public async Task AccountWithoutRequestIsNeverErased()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        var erasure = AccountSettingsServices.CreateAccountErasure(tools);

        Assert.False(await erasure.EraseAsync(account.Id));
        Assert.False(await erasure.EraseAsync(Guid.NewGuid()));
        Assert.Contains(account, tools.AccountRepository.SavedUsers);
    }

    // SS §2 : le code de l'application qui autorise la demande est noté avec elle : il ne resservira plus.
    [Fact]
    public async Task AuthenticatorCodeIsNotedWithTheRequest()
    {
        var activatedAccount = tools.AddActivatedAccount();

        var result = await RequestAsync(activatedAccount.UserAccount.Id, AccountPassword, tools.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<AccountDeletionResult.Requested>(result);
        Assert.Equal(tools.CurrentStep(), activatedAccount.UserAccount.LastAuthenticatorStep);
    }

    // Une autre requête se sert du même code juste avant l'enregistrement : la suppression n'est pas demandée (SS §2).
    [Fact]
    public async Task CodeUsedByAnotherRequestMeanwhileRefusesTheRequest()
    {
        var activatedAccount = tools.AddActivatedAccount();
        tools.AccountRepository.AuthenticatorStepUsedBeforeSave = tools.CurrentStep();

        var result = await RequestAsync(activatedAccount.UserAccount.Id, AccountPassword, tools.CalculateCurrentCode(activatedAccount.Secret));

        Assert.IsType<AccountDeletionResult.WrongPasswordOrCode>(result);
        Assert.Null(activatedAccount.UserAccount.DeletionRequestedAt);
        Assert.Null(tools.AccountRepository.AuthenticatorStepUsedBeforeSave);
    }
}
