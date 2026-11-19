using WalletFlow.Application.AccountSettings;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.Notifications;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Application.Emails.EmailRequestFields;

namespace WalletFlow.Application.Emails;

// Prépare les e-mails de la file qui préviennent sans lien : les alertes de sécurité (GR Notif), l'avis à l'ancienne adresse
// après un changement, et les e-mails de la suppression du compte (GR Suppression). Ils renvoient à la page de support (SS §9).
public class NoticeEmailPreparation
{
    private readonly IUserAccountRepository accountRepository;
    private readonly ISecurityAlertEmails securityAlertEmails;
    private readonly IAccountChangeEmails changeEmails;
    private readonly PublicWebsiteSettings websiteSettings;

    // Reçoit l'accès aux comptes, les textes des e-mails et l'adresse du site.
    public NoticeEmailPreparation(
        IUserAccountRepository accountRepository,
        ISecurityAlertEmails securityAlertEmails,
        IAccountChangeEmails changeEmails,
        PublicWebsiteSettings websiteSettings)
    {
        this.accountRepository = accountRepository;
        this.securityAlertEmails = securityAlertEmails;
        this.changeEmails = changeEmails;
        this.websiteSettings = websiteSettings;
    }

    // Prépare l'alerte de sécurité que demande ce type, à l'adresse actuelle du compte, avec l'heure de Paris de ce qui s'est
    // passé et l'adresse de la page de support. Un compte effacé depuis ne reçoit rien.
    public async Task<EmailToSend> PrepareSecurityAlertAsync(EmailRequest request, CancellationToken cancellationToken = default)
    {
        Func<string, DateTime, string, EmailMessage> createAlertEmail = request.Type switch
        {
            EmailRequestType.NewDevice => securityAlertEmails.CreateNewDeviceEmail,
            EmailRequestType.PasswordChange => securityAlertEmails.CreatePasswordChangedEmail,
            EmailRequestType.TwoFactorDeactivation => securityAlertEmails.CreateTwoFactorDeactivatedEmail,
            EmailRequestType.BackupCodeUse => securityAlertEmails.CreateBackupCodeUsedEmail,
            EmailRequestType.DeletionCancellation => securityAlertEmails.CreateDeletionCancelledEmail,
            _ => throw new InvalidOperationException("Ce type de demande d'e-mail n'est pas une alerte de sécurité.")
        };

        var account = await accountRepository.FindByIdAsync(ReadUserId(request), cancellationToken);
        if (account == null)
        {
            return EmailToSend.Nothing(request.UserId);
        }

        var alertEmail = createAlertEmail(
            account.Email,
            UsersCalendar.ConvertToUsersTime(request.RequestedAt),
            websiteSettings.BuildSupportPageAddress());

        return EmailToSend.Send(alertEmail, account.Id);
    }

    // Prévient l'ancienne adresse, que porte la demande, que le changement est confirmé (GR Compte).
    public EmailToSend PrepareEmailChangedNotice(EmailRequest request)
    {
        var noticeEmail = changeEmails.CreateEmailChangedEmail(ReadEmailAddress(request), websiteSettings.BuildSupportPageAddress());

        return EmailToSend.Send(noticeEmail, request.UserId);
    }

    // Confirme la demande de suppression à l'adresse actuelle du compte, avec le jour de Paris où il sera effacé. Une demande
    // déjà annulée par une connexion, ou un compte déjà effacé, ne reçoit rien : il n'y a plus rien à confirmer.
    public async Task<EmailToSend> PrepareDeletionRequestAsync(EmailRequest request, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.FindByIdAsync(ReadUserId(request), cancellationToken);
        if (account?.DeletionRequestedAt is not DateTime deletionRequestedAt)
        {
            return EmailToSend.Nothing(request.UserId);
        }

        var erasureMoment = AccountDeletionRules.FindErasureMoment(deletionRequestedAt);
        var requestEmail = changeEmails.CreateDeletionRequestedEmail(
            account.Email,
            DateOnly.FromDateTime(UsersCalendar.ConvertToUsersTime(erasureMoment)),
            websiteSettings.BuildSupportPageAddress());

        return EmailToSend.Send(requestEmail, account.Id);
    }

    // Annonce l'effacement définitif à l'adresse que porte la demande : le compte n'existe plus (GR Suppression).
    public EmailToSend PrepareErasureNotice(EmailRequest request)
    {
        return EmailToSend.Send(changeEmails.CreateAccountErasedEmail(ReadEmailAddress(request)), null);
    }
}
