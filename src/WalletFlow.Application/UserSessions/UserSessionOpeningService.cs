using WalletFlow.Application.Emails;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserSessions;

// Ouvre la session d'une connexion réussie, avec ou sans double authentification (GR Auth, SS §3).
public class UserSessionOpeningService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly IUserSessionRepository sessionRepository;
    private readonly ITwoFactorAuthenticationRepository twoFactorRepository;
    private readonly IEmailRequestRepository emailRequests;
    private readonly DatabaseChangesSavingService savingService;
    private readonly SecurityJournalService securityJournal;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly TimeProvider clock;

    // Reçoit les outils dont l'ouverture d'une session a besoin.
    public UserSessionOpeningService(
        IUserAccountRepository accountRepository,
        IUserSessionRepository sessionRepository,
        ITwoFactorAuthenticationRepository twoFactorRepository,
        IEmailRequestRepository emailRequests,
        DatabaseChangesSavingService savingService,
        SecurityJournalService securityJournal,
        ISecureTokenGenerator tokenGenerator,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.sessionRepository = sessionRepository;
        this.twoFactorRepository = twoFactorRepository;
        this.emailRequests = emailRequests;
        this.savingService = savingService;
        this.securityJournal = securityJournal;
        this.tokenGenerator = tokenGenerator;
        this.clock = clock;
    }

    // Ouvre une session avec un nouvel identifiant (SS §3), et enregistre l'appareil s'il est nouveau.
    // Renvoie null si la connexion ne peut plus s'enregistrer : rien n'est alors ouvert.
    public async Task<OpenedUserSession?> TryOpenSessionAsync(SuccessfulLogin login)
    {
        var newSessionToken = tokenGenerator.GenerateToken();
        var session = CreateSession(newSessionToken, login.AccountId);

        // L'appareil garde son jeton s'il est déjà connu du compte. Sinon, il en reçoit un nouveau.
        Device? device = null;
        var deviceToken = await FindKnownDeviceTokenAsync(login.AccountId, login.DeviceToken);
        if (deviceToken == null)
        {
            var newDeviceToken = tokenGenerator.GenerateToken();
            device = CreateDevice(newDeviceToken, login.AccountId);
            deviceToken = newDeviceToken.Token;
        }

        var saving = await savingService.SaveWithRetriesAsync(
            () => PrepareLoginAsync(login, session, device),
            LoginSaving.Refused);

        if (saving == LoginSaving.Refused)
        {
            return null;
        }

        await CloseOldSessionAsync(login.SessionToken);

        return new OpenedUserSession
        {
            SessionToken = newSessionToken.Token,
            DeviceToken = deviceToken,
            HasCancelledDeletion = saving == LoginSaving.SavedWithDeletionCancelled
        };
    }

    // Un passage : lit le compte, vérifie que ce qui vient d'être contrôlé est toujours valable, puis prépare en une fois
    // les compteurs remis à zéro, la nouvelle session, le nouvel appareil s'il y en a un avec son alerte, le code de secours
    // utilisé s'il y en a un avec son alerte, la suppression annulée s'il y en avait une avec son alerte, et l'événement du journal.
    // Refuse si le compte a disparu, effacé par exemple, ou si ce qui vient d'être vérifié n'est plus valable.
    private async Task<PreparedChanges<LoginSaving>> PrepareLoginAsync(
        SuccessfulLogin login,
        UserSession session,
        Device? device)
    {
        var account = await accountRepository.FindByIdAsync(login.AccountId);
        if (account == null || !AccountStillMatches(account, login))
        {
            return PreparedChanges<LoginSaving>.NothingToSave(LoginSaving.Refused);
        }

        if (login.BackupCodeId != null)
        {
            var backupCode = await twoFactorRepository.FindBackupCodeAsync(login.BackupCodeId.Value);
            if (backupCode == null || backupCode.UsedAt != null)
            {
                return PreparedChanges<LoginSaving>.NothingToSave(LoginSaving.Refused);
            }

            // L'enregistrement refusera tout si une autre requête vient de se servir de ce code.
            backupCode.MarkAsUsed(session.LastActivityAt);
            emailRequests.PrepareRequest(EmailRequest.ForBackupCodeUse(login.AccountId, login.IpAddress, session.LastActivityAt));
        }

        account.ResetLoginCounters();

        // Une connexion réussie annule la suppression demandée, avec l'alerte qui le dit (GR Suppression). Elle l'annule tant que le
        // compte n'est pas effacé, donc jusqu'au passage de la tâche horaire qui suit les 30 jours.
        var cancelsDeletion = account.DeletionRequestedAt != null;
        if (cancelsDeletion)
        {
            account.CancelDeletion();
            emailRequests.PrepareRequest(EmailRequest.ForDeletionCancellation(login.AccountId, login.IpAddress, session.LastActivityAt));
        }

        // Le compte est contrôlé même si ses compteurs n'ont pas changé : ce contrôle vérifie en base que personne ne l'a changé
        // depuis sa lecture, mot de passe compris. Sinon, une session survivrait à une récupération du mot de passe.
        accountRepository.PrepareUnchangedCheck(account);
        sessionRepository.PrepareSession(session);

        if (device != null)
        {
            await PrepareNewDeviceAsync(device, login, session.LastActivityAt);
        }

        securityJournal.PrepareEvent(
            SecurityEventType.Login,
            SecurityEventResult.Success,
            login.AccountId,
            login.IpAddress);

        return PreparedChanges<LoginSaving>.SaveThenReturn(cancelsDeletion ? LoginSaving.SavedWithDeletionCancelled : LoginSaving.Saved);
    }

    // Prépare le nouvel appareil, et l'alerte « nouvel appareil » s'il n'est pas le premier du compte : la toute première
    // connexion d'un compte ne la déclenche pas (GR Notif). Deux premières connexions simultanées ne se croient pas
    // toutes les deux la première : le compte, contrôlé à l'enregistrement, fait passer la seconde après la première,
    // et elle relit alors l'appareil de la première.
    private async Task PrepareNewDeviceAsync(Device device, SuccessfulLogin login, DateTime now)
    {
        if (await sessionRepository.HasDeviceAsync(login.AccountId))
        {
            emailRequests.PrepareRequest(EmailRequest.ForNewDevice(login.AccountId, login.IpAddress, now));
        }

        sessionRepository.PrepareDevice(device);
    }

    // Une récupération du mot de passe (SS §3), une activation ou une désactivation a pu passer pendant les vérifications.
    // Une connexion sans code ne s'enregistre donc jamais sur un compte qui vient d'activer la double authentification.
    private static bool AccountStillMatches(User account, SuccessfulLogin login)
    {
        return account.PasswordHash == login.PasswordHash
            && account.EncryptedTotpSecret == login.EncryptedTotpSecret;
    }

    // Crée la session à enregistrer. Seule l'empreinte de son identifiant va en base.
    private UserSession CreateSession(GeneratedSecureToken newSessionToken, Guid accountId)
    {
        return new UserSession
        {
            Id = newSessionToken.Hash,
            LastActivityAt = clock.GetUtcNow().UtcDateTime,
            UserId = accountId
        };
    }

    // Renvoie le jeton d'appareil du cookie si ce compte le connaît déjà.
    // Renvoie null si le navigateur n'a pas de jeton d'appareil, ou si ce jeton n'est pas celui de ce compte.
    private async Task<string?> FindKnownDeviceTokenAsync(Guid accountId, string? deviceToken)
    {
        if (deviceToken == null)
        {
            return null;
        }

        var deviceTokenHash = tokenGenerator.HashToken(deviceToken);

        if (!await sessionRepository.DeviceBelongsToAccountAsync(deviceTokenHash, accountId))
        {
            return null;
        }

        return deviceToken;
    }

    // Crée l'appareil à enregistrer. Seule l'empreinte de son jeton va en base.
    private static Device CreateDevice(GeneratedSecureToken newDeviceToken, Guid accountId)
    {
        return new Device { DeviceToken = newDeviceToken.Hash, UserId = accountId };
    }

    // Le navigateur reçoit une nouvelle session : l'ancienne ne doit pas rester ouverte sans que personne ne s'en serve.
    private async Task CloseOldSessionAsync(string? sessionToken)
    {
        if (sessionToken == null)
        {
            return;
        }

        var oldSessionTokenHash = tokenGenerator.HashToken(sessionToken);
        await sessionRepository.DeleteAsync(oldSessionTokenHash);
    }

    // Ce que l'enregistrement d'une connexion a fait.
    private enum LoginSaving
    {
        Refused,
        Saved,
        SavedWithDeletionCancelled
    }
}
