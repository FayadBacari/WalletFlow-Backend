using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.AccountSettings;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.Notifications;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.Support;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Emails;

// Cas d'utilisation : envoie les e-mails de la file, un essai à la fois (SS §9). Le lien d'un e-mail est créé au moment
// de l'essai : la file n'en garde aucun, et le lien d'un nouvel essai remplace celui de l'essai raté.
// Une demande sort de la file dès que son e-mail est parti, ou quand il n'y a plus rien à envoyer. Un envoi raté est
// réessayé plus tard, 5 fois au plus. Si le serveur s'arrête entre l'envoi et la sortie de la file, l'e-mail repartira :
// mieux vaut un e-mail en double qu'un e-mail perdu.
// Le temps de ces envois ne compte dans aucune réponse : la route a déjà répondu, de la même façon pour toutes les adresses (SS §2).
public class EmailRequestProcessingService
{
    // La page du site où écrire au support : les e-mails de sécurité y renvoient (SS §9).
    public const string SupportPagePath = "/support";

    private readonly IEmailRequestRepository emailRequests;
    private readonly IUserAccountRepository accountRepository;
    private readonly IAlertRepository alertRepository;
    private readonly ISupportRequestRepository supportRepository;
    private readonly IDatabaseUnitOfWork unitOfWork;
    private readonly SecurityJournalService securityJournal;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly IEmailSender emailSender;
    private readonly IAccountRecoveryEmails recoveryEmails;
    private readonly IAccountChangeEmails changeEmails;
    private readonly ISecurityAlertEmails securityAlertEmails;
    private readonly IAlertEmails alertEmails;
    private readonly ISupportEmails supportEmails;
    private readonly PublicWebsiteSettings websiteSettings;
    private readonly TimeProvider clock;

    // Reçoit les outils dont ces e-mails ont besoin.
    public EmailRequestProcessingService(
        IEmailRequestRepository emailRequests,
        IUserAccountRepository accountRepository,
        IAlertRepository alertRepository,
        ISupportRequestRepository supportRepository,
        IDatabaseUnitOfWork unitOfWork,
        SecurityJournalService securityJournal,
        ISecureTokenGenerator tokenGenerator,
        IEmailSender emailSender,
        IAccountRecoveryEmails recoveryEmails,
        IAccountChangeEmails changeEmails,
        ISecurityAlertEmails securityAlertEmails,
        IAlertEmails alertEmails,
        ISupportEmails supportEmails,
        PublicWebsiteSettings websiteSettings,
        TimeProvider clock)
    {
        this.emailRequests = emailRequests;
        this.accountRepository = accountRepository;
        this.alertRepository = alertRepository;
        this.supportRepository = supportRepository;
        this.unitOfWork = unitOfWork;
        this.securityJournal = securityJournal;
        this.tokenGenerator = tokenGenerator;
        this.emailSender = emailSender;
        this.recoveryEmails = recoveryEmails;
        this.changeEmails = changeEmails;
        this.securityAlertEmails = securityAlertEmails;
        this.alertEmails = alertEmails;
        this.supportEmails = supportEmails;
        this.websiteSettings = websiteSettings;
        this.clock = clock;
    }

    // Donne l'identifiant de la prochaine demande dont l'heure d'essai est venue, ou null.
    public async Task<Guid?> FindNextDueRequestIdAsync()
    {
        return await emailRequests.FindNextDueIdAsync(clock.GetUtcNow().UtcDateTime);
    }

    // Fait un essai pour cette demande : envoie son e-mail, puis la sort de la file, ou note l'essai raté.
    public async Task<EmailRequestOutcome> ProcessAsync(Guid requestId)
    {
        var request = await emailRequests.FindAsync(requestId);
        if (request == null)
        {
            return new EmailRequestOutcome.NoLongerQueued();
        }

        var attempt = await TrySendAsync(request);

        if (attempt.HasFailed)
        {
            return await RecordFailedTryAsync(request, attempt);
        }

        return await RemoveAsync(request, attempt);
    }

    // Note un essai raté pour une raison imprévue, que la tâche de fond a déjà écrite au journal technique.
    // Sans cela, une demande qui échouerait toujours de la même façon resterait en tête de la file et bloquerait les autres.
    public async Task<EmailRequestOutcome> RecordUnexpectedFailureAsync(Guid requestId)
    {
        var request = await emailRequests.FindAsync(requestId);
        if (request == null)
        {
            return new EmailRequestOutcome.NoLongerQueued();
        }

        var recoveryResult = request.Type == EmailRequestType.PasswordRecovery ? SecurityEventResult.Failure : (SecurityEventResult?)null;

        return await RecordFailedTryAsync(request, SendingAttempt.Failed(request.UserId, recoveryResult));
    }

    // Envoie l'e-mail que la demande attend.
    private async Task<SendingAttempt> TrySendAsync(EmailRequest request)
    {
        return request.Type switch
        {
            EmailRequestType.PasswordRecovery => await TrySendRecoveryEmailAsync(request),
            EmailRequestType.AccountUnlock => await TrySendUnlockEmailAsync(request),
            EmailRequestType.EmailChange => await TrySendEmailChangeEmailAsync(request),
            EmailRequestType.EmailChangedNotice => await TrySendEmailAsync(
                changeEmails.CreateEmailChangedEmail(ReadEmailAddress(request), BuildSupportPageAddress()), request.UserId, null),
            EmailRequestType.NewDevice => await TrySendSecurityAlertAsync(request, securityAlertEmails.CreateNewDeviceEmail),
            EmailRequestType.PasswordChange => await TrySendSecurityAlertAsync(request, securityAlertEmails.CreatePasswordChangedEmail),
            EmailRequestType.TwoFactorDeactivation => await TrySendSecurityAlertAsync(
                request, securityAlertEmails.CreateTwoFactorDeactivatedEmail),
            EmailRequestType.BackupCodeUse => await TrySendSecurityAlertAsync(request, securityAlertEmails.CreateBackupCodeUsedEmail),
            EmailRequestType.TwoFactorDeactivationLink => await TrySendTwoFactorLinkEmailAsync(request),
            EmailRequestType.SupportResponse => await TrySendSupportResponseEmailAsync(request),
            EmailRequestType.Alert => await TrySendAlertEmailAsync(request),
            EmailRequestType.DeletionRequest => await TrySendDeletionRequestEmailAsync(request),
            EmailRequestType.DeletionCancellation => await TrySendSecurityAlertAsync(request, securityAlertEmails.CreateDeletionCancelledEmail),
            EmailRequestType.AccountErasure => await TrySendEmailAsync(changeEmails.CreateAccountErasedEmail(ReadEmailAddress(request)), null, null),
            _ => throw new InvalidOperationException("Ce type de demande d'e-mail n'est pas prévu.")
        };
    }

    // Choisit l'e-mail selon l'état du compte. Une adresse sans compte ne reçoit rien, et laisse une trace sans identifiant.
    private async Task<SendingAttempt> TrySendRecoveryEmailAsync(EmailRequest request)
    {
        var existingAccount = await accountRepository.FindByEmailAsync(ReadEmailAddress(request));
        if (existingAccount == null)
        {
            return SendingAttempt.Finished(null, SecurityEventResult.Failure);
        }

        if (!existingAccount.IsEmailConfirmed)
        {
            var invitationEmail = recoveryEmails.CreateSignupInvitationEmail(existingAccount.Email);
            return await TrySendEmailAsync(invitationEmail, existingAccount.Id, SecurityEventResult.Failure);
        }

        var recoveryToken = await ReplaceLinkAsync(
            existingAccount.Id,
            EmailTokenType.PasswordRecovery,
            EmailTokenValidityDurations.PasswordRecoveryLink,
            null);
        var recoveryEmail = recoveryEmails.CreateRecoveryEmail(
            existingAccount.Email,
            websiteSettings.BuildLink("/reset-password", recoveryToken.Token));

        return await TrySendEmailAsync(recoveryEmail, existingAccount.Id, SecurityEventResult.Success);
    }

    // Envoie le lien de déblocage, sauf si le compte a disparu ou n'est plus bloqué : il n'y a alors rien à débloquer.
    private async Task<SendingAttempt> TrySendUnlockEmailAsync(EmailRequest request)
    {
        var lockedAccount = await accountRepository.FindByIdAsync(ReadUserId(request));
        if (lockedAccount == null
            || !lockedAccount.IsLockedAt(clock.GetUtcNow().UtcDateTime))
        {
            return SendingAttempt.Finished(request.UserId, null);
        }

        var unlockToken = await ReplaceLinkAsync(
            lockedAccount.Id,
            EmailTokenType.AccountUnlock,
            EmailTokenValidityDurations.AccountUnlockLink,
            null);
        var unlockEmail = recoveryEmails.CreateUnlockEmail(
            lockedAccount.Email,
            websiteSettings.BuildLink("/unlock-account", unlockToken.Token),
            AccountLockoutRules.CalculateLockoutDuration(lockedAccount.LockoutCount));

        return await TrySendEmailAsync(unlockEmail, lockedAccount.Id, null);
    }

    // Une adresse libre reçoit le lien qui la confirme. Une adresse déjà utilisée ne reçoit pas de lien : son propriétaire est prévenu.
    // L'utilisateur a déjà reçu la même réponse dans les deux cas (SS §2). Un compte effacé depuis sa demande ne reçoit rien.
    private async Task<SendingAttempt> TrySendEmailChangeEmailAsync(EmailRequest request)
    {
        var account = await accountRepository.FindByIdAsync(ReadUserId(request));
        if (account == null)
        {
            return SendingAttempt.Finished(request.UserId, null);
        }

        var newEmail = ReadEmailAddress(request);
        if (await accountRepository.FindByEmailAsync(newEmail) != null)
        {
            return await TrySendEmailAsync(changeEmails.CreateAddressAlreadyUsedEmail(newEmail), account.Id, null);
        }

        var linkToken = await ReplaceLinkAsync(
            account.Id,
            EmailTokenType.EmailChange,
            EmailTokenValidityDurations.EmailChangeLink,
            newEmail);
        var linkEmail = changeEmails.CreateEmailChangeLinkEmail(
            newEmail,
            websiteSettings.BuildLink("/confirm-email-change", linkToken.Token));

        return await TrySendEmailAsync(linkEmail, account.Id, null);
    }

    // Envoie une alerte de sécurité à l'adresse actuelle du compte, avec l'heure de Paris de ce qui s'est passé et l'adresse
    // de la page de support. Un compte effacé depuis ne reçoit rien.
    private async Task<SendingAttempt> TrySendSecurityAlertAsync(
        EmailRequest request,
        Func<string, DateTime, string, EmailMessage> createAlertEmail)
    {
        var account = await accountRepository.FindByIdAsync(ReadUserId(request));
        if (account == null)
        {
            return SendingAttempt.Finished(request.UserId, null);
        }

        var alertEmail = createAlertEmail(
            account.Email,
            UsersCalendar.ConvertToUsersTime(request.RequestedAt),
            BuildSupportPageAddress());

        return await TrySendEmailAsync(alertEmail, account.Id, null);
    }

    // Envoie le lien du support qui désactive la double authentification, à l'adresse actuelle du compte. Un compte effacé,
    // ou dont la double authentification est déjà désactivée, ne reçoit rien : il n'y a plus rien à désactiver.
    private async Task<SendingAttempt> TrySendTwoFactorLinkEmailAsync(EmailRequest request)
    {
        var account = await accountRepository.FindByIdAsync(ReadUserId(request));
        if (account == null || !account.IsTwoFactorEnabled)
        {
            return SendingAttempt.Finished(request.UserId, null);
        }

        var linkToken = await ReplaceLinkAsync(
            account.Id,
            EmailTokenType.TwoFactorDeactivation,
            EmailTokenValidityDurations.TwoFactorDeactivationLink,
            null);
        var linkEmail = recoveryEmails.CreateTwoFactorDeactivationLinkEmail(
            account.Email,
            websiteSettings.BuildLink("/disable-two-factor", linkToken.Token),
            BuildSupportPageAddress());

        return await TrySendEmailAsync(linkEmail, account.Id, null);
    }

    // Envoie une réponse du support, avec son texte, à l'adresse lue dans sa demande au moment de l'envoi : celle du compte,
    // ou celle du visiteur (GR Admin). Une réponse effacée depuis, avec sa demande, ne part plus.
    private async Task<SendingAttempt> TrySendSupportResponseEmailAsync(EmailRequest request)
    {
        var content = await supportRepository.ReadResponseEmailAsync(ReadSupportResponseId(request));
        if (content == null)
        {
            return SendingAttempt.Finished(null, null);
        }

        var responseEmail = supportEmails.CreateResponseEmail(
            content.RecipientAddress,
            UsersCalendar.ConvertToUsersTime(content.RequestSentAt),
            content.ResponseText,
            BuildSupportPageAddress());

        return await TrySendEmailAsync(responseEmail, content.AccountId, null);
    }

    // Confirme la demande de suppression à l'adresse actuelle du compte, avec le jour de Paris où il sera effacé. Une demande
    // déjà annulée par une connexion, ou un compte déjà effacé, ne reçoit rien : il n'y a plus rien à confirmer.
    private async Task<SendingAttempt> TrySendDeletionRequestEmailAsync(EmailRequest request)
    {
        var account = await accountRepository.FindByIdAsync(ReadUserId(request));
        if (account?.DeletionRequestedAt is not DateTime deletionRequestedAt)
        {
            return SendingAttempt.Finished(request.UserId, null);
        }

        var erasureMoment = AccountDeletionRules.FindErasureMoment(deletionRequestedAt);
        var requestEmail = changeEmails.CreateDeletionRequestedEmail(
            account.Email,
            DateOnly.FromDateTime(UsersCalendar.ConvertToUsersTime(erasureMoment)),
            BuildSupportPageAddress());

        return await TrySendEmailAsync(requestEmail, account.Id, null);
    }

    // Envoie l'e-mail d'une alerte à l'adresse actuelle du compte. Ce qu'il dit est lu au moment de l'envoi : une catégorie
    // ou un sac renommé entre-temps porte son nouveau nom. Un compte effacé, ou une alerte effacée, ne reçoit rien.
    private async Task<SendingAttempt> TrySendAlertEmailAsync(EmailRequest request)
    {
        var account = await accountRepository.FindByIdAsync(ReadUserId(request));
        if (account == null)
        {
            return SendingAttempt.Finished(request.UserId, null);
        }

        var content = await alertRepository.ReadEmailContentAsync(ReadAlertId(request));
        if (content == null)
        {
            return SendingAttempt.Finished(account.Id, null);
        }

        return await TrySendEmailAsync(alertEmails.CreateEmail(account.Email, content), account.Id, null);
    }

    // Donne l'adresse de la page de support, sur le domaine de WalletFlow, en HTTPS (SS §9).
    private string BuildSupportPageAddress()
    {
        return websiteSettings.BuildPageAddress(SupportPagePath);
    }

    // Tire un nouveau jeton et remplace l'ancien lien du même type, qui ne marche plus (SS §2). Seule l'empreinte va en base.
    // La nouvelle adresse n'accompagne que le lien d'un changement d'adresse : sinon, elle est vide (DD §4.2).
    private async Task<GeneratedSecureToken> ReplaceLinkAsync(Guid accountId, EmailTokenType linkType, TimeSpan validity, string? newEmail)
    {
        var linkToken = tokenGenerator.GenerateToken();

        await accountRepository.ReplaceTokenAsync(new EmailToken
        {
            Type = linkType,
            TokenHash = linkToken.Hash,
            ExpiresAt = clock.GetUtcNow().UtcDateTime + validity,
            NewEmail = newEmail,
            UserId = accountId
        });

        return linkToken;
    }

    // Envoie un e-mail. S'il ne peut pas partir, la demande reste dans la file pour un autre essai.
    // Une récupération donne le résultat à noter au journal quand elle sortira de la file : un échec si l'e-mail ne part jamais.
    private async Task<SendingAttempt> TrySendEmailAsync(EmailMessage message, Guid? accountId, SecurityEventResult? recoveryResultIfSent)
    {
        try
        {
            await emailSender.SendAsync(message);
            return SendingAttempt.Finished(accountId, recoveryResultIfSent);
        }
        catch (EmailCouldNotBeSentException)
        {
            var recoveryResultIfGivenUp = recoveryResultIfSent == null ? (SecurityEventResult?)null : SecurityEventResult.Failure;
            return SendingAttempt.Failed(accountId, recoveryResultIfGivenUp);
        }
    }

    // Sort la demande de la file, avec la trace d'une récupération au journal, dans la même transaction.
    private async Task<EmailRequestOutcome> RemoveAsync(EmailRequest request, SendingAttempt attempt)
    {
        emailRequests.PrepareRemoval(request);
        PrepareRecoveryEvent(request, attempt);

        if (!await unitOfWork.TrySaveChangesAsync())
        {
            return new EmailRequestOutcome.NoLongerQueued();
        }

        return new EmailRequestOutcome.Finished();
    }

    // Note l'essai raté, et l'heure de l'essai suivant. Après le dernier, la demande sort de la file sans e-mail.
    private async Task<EmailRequestOutcome> RecordFailedTryAsync(EmailRequest request, SendingAttempt attempt)
    {
        if (request.RecordFailedTry(clock.GetUtcNow().UtcDateTime))
        {
            if (!await unitOfWork.TrySaveChangesAsync())
            {
                return new EmailRequestOutcome.NoLongerQueued();
            }

            return new EmailRequestOutcome.WillRetry
            {
                Type = request.Type,
                UserId = attempt.AccountId,
                FailedTryCount = request.FailedTryCount,
                NextTryAt = request.NextTryAt
            };
        }

        emailRequests.PrepareRemoval(request);
        PrepareRecoveryEvent(request, attempt);

        if (!await unitOfWork.TrySaveChangesAsync())
        {
            return new EmailRequestOutcome.NoLongerQueued();
        }

        return new EmailRequestOutcome.GivenUp { Type = request.Type, UserId = attempt.AccountId };
    }

    // Une récupération laisse sa trace au journal de sécurité, sans jamais l'adresse e-mail (SS §7 et §11).
    // Les autres e-mails n'en laissent pas : ce qui les a demandés est déjà noté.
    private void PrepareRecoveryEvent(EmailRequest request, SendingAttempt attempt)
    {
        if (attempt.RecoveryResult is SecurityEventResult recoveryResult)
        {
            securityJournal.PrepareEvent(SecurityEventType.PasswordRecovery, recoveryResult, attempt.AccountId, request.IpAddress);
        }
    }

    // L'adresse que porte la demande. La base refuse une récupération ou un changement d'adresse sans elle.
    private static string ReadEmailAddress(EmailRequest request)
    {
        if (request.EmailAddress is not string emailAddress)
        {
            throw new InvalidOperationException("Cette demande d'e-mail n'a pas d'adresse.");
        }

        return emailAddress;
    }

    // Le compte que vise la demande. La base refuse toute demande sans compte, sauf une récupération et une réponse au support.
    private static Guid ReadUserId(EmailRequest request)
    {
        if (request.UserId is not Guid userId)
        {
            throw new InvalidOperationException("Cette demande d'e-mail ne vise aucun compte.");
        }

        return userId;
    }

    // La réponse qu'envoie la demande. La base refuse l'e-mail d'une réponse sans elle.
    private static Guid ReadSupportResponseId(EmailRequest request)
    {
        if (request.SupportResponseId is not Guid responseId)
        {
            throw new InvalidOperationException("Cette demande d'e-mail n'envoie aucune réponse du support.");
        }

        return responseId;
    }

    // L'alerte qu'annonce la demande. La base refuse un e-mail d'alerte sans elle.
    private static Guid ReadAlertId(EmailRequest request)
    {
        if (request.AlertId is not Guid alertId)
        {
            throw new InvalidOperationException("Cette demande d'e-mail n'annonce aucune alerte.");
        }

        return alertId;
    }

    // Ce qu'un essai d'envoi a donné.
    private sealed class SendingAttempt
    {
        public required bool HasFailed { get; init; }

        // Le compte à qui l'e-mail était destiné, s'il y en a un.
        public required Guid? AccountId { get; init; }

        // Pour une récupération seulement : le résultat à noter au journal quand la demande sortira de la file.
        public required SecurityEventResult? RecoveryResult { get; init; }

        // L'e-mail est parti, ou il n'y a rien à envoyer : la demande peut sortir de la file.
        public static SendingAttempt Finished(Guid? accountId, SecurityEventResult? recoveryResult)
        {
            return new SendingAttempt { HasFailed = false, AccountId = accountId, RecoveryResult = recoveryResult };
        }

        // L'e-mail n'est pas parti : la demande attend un autre essai.
        public static SendingAttempt Failed(Guid? accountId, SecurityEventResult? recoveryResultIfGivenUp)
        {
            return new SendingAttempt { HasFailed = true, AccountId = accountId, RecoveryResult = recoveryResultIfGivenUp };
        }
    }
}
