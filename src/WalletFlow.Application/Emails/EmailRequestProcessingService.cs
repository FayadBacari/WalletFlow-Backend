using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Emails;

// Cas d'utilisation : envoie les e-mails de la file, un essai à la fois (SS §9). Chaque e-mail est préparé au moment de
// l'essai par la préparation de son type : les liens, les avis, ou les alertes et le support.
// Une demande sort de la file dès que son e-mail est parti, ou quand il n'y a plus rien à envoyer. Un envoi raté est
// réessayé plus tard, 5 fois au plus. Si le serveur s'arrête entre l'envoi et la sortie de la file, l'e-mail repartira :
// mieux vaut un e-mail en double qu'un e-mail perdu.
// Le temps de ces envois ne compte dans aucune réponse : la route a déjà répondu, de la même façon pour toutes les adresses (SS §2).
public class EmailRequestProcessingService
{
    private readonly IEmailRequestRepository emailRequests;
    private readonly IDatabaseUnitOfWork unitOfWork;
    private readonly SecurityJournalService securityJournal;
    private readonly IEmailSender emailSender;
    private readonly LinkEmailPreparation linkEmails;
    private readonly NoticeEmailPreparation noticeEmails;
    private readonly AlertAndSupportEmailPreparation alertAndSupportEmails;
    private readonly TimeProvider clock;

    // Reçoit la file, l'enregistrement, le journal de sécurité, l'envoi des e-mails, leurs trois préparations et l'horloge.
    public EmailRequestProcessingService(
        IEmailRequestRepository emailRequests,
        IDatabaseUnitOfWork unitOfWork,
        SecurityJournalService securityJournal,
        IEmailSender emailSender,
        LinkEmailPreparation linkEmails,
        NoticeEmailPreparation noticeEmails,
        AlertAndSupportEmailPreparation alertAndSupportEmails,
        TimeProvider clock)
    {
        this.emailRequests = emailRequests;
        this.unitOfWork = unitOfWork;
        this.securityJournal = securityJournal;
        this.emailSender = emailSender;
        this.linkEmails = linkEmails;
        this.noticeEmails = noticeEmails;
        this.alertAndSupportEmails = alertAndSupportEmails;
        this.clock = clock;
    }

    // Donne l'identifiant de la prochaine demande dont l'heure d'essai est venue, ou null.
    public async Task<Guid?> FindNextDueRequestIdAsync(CancellationToken cancellationToken = default)
    {
        return await emailRequests.FindNextDueIdAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);
    }

    // Fait un essai pour cette demande : envoie son e-mail, puis la sort de la file, ou note l'essai raté.
    public async Task<EmailRequestOutcome> ProcessAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await emailRequests.FindAsync(requestId, cancellationToken);
        if (request == null)
        {
            return new EmailRequestOutcome.NoLongerQueued();
        }

        var attempt = await TrySendAsync(request, cancellationToken);

        if (attempt.HasFailed)
        {
            return await RecordFailedTryAsync(request, attempt, CancellationToken.None);
        }

        // L'essai d'envoi est fait : sa trace ne s'annule plus, sinon un e-mail parti repartirait au passage suivant.
        return await RemoveAsync(request, attempt, CancellationToken.None);
    }

    // Note un essai raté pour une raison imprévue, que la tâche de fond a déjà écrite au journal technique.
    // Sans cela, une demande qui échouerait toujours de la même façon resterait en tête de la file et bloquerait les autres.
    public async Task<EmailRequestOutcome> RecordUnexpectedFailureAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await emailRequests.FindAsync(requestId, cancellationToken);
        if (request == null)
        {
            return new EmailRequestOutcome.NoLongerQueued();
        }

        var recoveryResult = request.Type == EmailRequestType.PasswordRecovery ? SecurityEventResult.Failure : (SecurityEventResult?)null;

        return await RecordFailedTryAsync(request, SendingAttempt.Failed(request.UserId, recoveryResult), cancellationToken);
    }

    // Prépare l'e-mail que la demande attend, puis l'envoie s'il y en a un.
    private async Task<SendingAttempt> TrySendAsync(EmailRequest request, CancellationToken cancellationToken = default)
    {
        var email = await PrepareAsync(request, cancellationToken);

        if (email.Message == null)
        {
            return SendingAttempt.Finished(email.AccountId, email.RecoveryResult);
        }

        return await TrySendEmailAsync(email.Message, email.AccountId, email.RecoveryResult, cancellationToken);
    }

    // Demande l'e-mail à la préparation de son type.
    private async Task<EmailToSend> PrepareAsync(EmailRequest request, CancellationToken cancellationToken = default)
    {
        return request.Type switch
        {
            EmailRequestType.PasswordRecovery => await linkEmails.PrepareRecoveryAsync(request, cancellationToken),
            EmailRequestType.AccountUnlock => await linkEmails.PrepareUnlockAsync(request, cancellationToken),
            EmailRequestType.EmailChange => await linkEmails.PrepareEmailChangeAsync(request, cancellationToken),
            EmailRequestType.TwoFactorDeactivationLink => await linkEmails.PrepareTwoFactorLinkAsync(request, cancellationToken),
            EmailRequestType.EmailChangedNotice => noticeEmails.PrepareEmailChangedNotice(request),
            EmailRequestType.NewDevice
                or EmailRequestType.PasswordChange
                or EmailRequestType.TwoFactorDeactivation
                or EmailRequestType.BackupCodeUse
                or EmailRequestType.DeletionCancellation => await noticeEmails.PrepareSecurityAlertAsync(request, cancellationToken),
            EmailRequestType.DeletionRequest => await noticeEmails.PrepareDeletionRequestAsync(request, cancellationToken),
            EmailRequestType.AccountErasure => noticeEmails.PrepareErasureNotice(request),
            EmailRequestType.SupportResponse => await alertAndSupportEmails.PrepareSupportResponseAsync(request, cancellationToken),
            EmailRequestType.Alert => await alertAndSupportEmails.PrepareAlertAsync(request, cancellationToken),
            _ => throw new InvalidOperationException("Ce type de demande d'e-mail n'est pas prévu.")
        };
    }

    // Envoie un e-mail. S'il ne peut pas partir, la demande reste dans la file pour un autre essai.
    // Une récupération donne le résultat à noter au journal quand elle sortira de la file : un échec si l'e-mail ne part jamais.
    private async Task<SendingAttempt> TrySendEmailAsync(EmailMessage message, Guid? accountId, SecurityEventResult? recoveryResultIfSent, CancellationToken cancellationToken = default)
    {
        try
        {
            await emailSender.SendAsync(message, cancellationToken);
            return SendingAttempt.Finished(accountId, recoveryResultIfSent);
        }
        catch (EmailCouldNotBeSentException)
        {
            var recoveryResultIfGivenUp = recoveryResultIfSent == null ? (SecurityEventResult?)null : SecurityEventResult.Failure;
            return SendingAttempt.Failed(accountId, recoveryResultIfGivenUp);
        }
    }

    // Sort la demande de la file, avec la trace d'une récupération au journal, dans la même transaction.
    private async Task<EmailRequestOutcome> RemoveAsync(EmailRequest request, SendingAttempt attempt, CancellationToken cancellationToken = default)
    {
        emailRequests.PrepareRemoval(request);
        PrepareRecoveryEvent(request, attempt);

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
        {
            return new EmailRequestOutcome.NoLongerQueued();
        }

        return new EmailRequestOutcome.Finished();
    }

    // Note l'essai raté, et l'heure de l'essai suivant. Après le dernier, la demande sort de la file sans e-mail.
    private async Task<EmailRequestOutcome> RecordFailedTryAsync(EmailRequest request, SendingAttempt attempt, CancellationToken cancellationToken = default)
    {
        if (request.RecordFailedTry(clock.GetUtcNow().UtcDateTime))
        {
            if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
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

        if (!await unitOfWork.TrySaveChangesAsync(cancellationToken))
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
