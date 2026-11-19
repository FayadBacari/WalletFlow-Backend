using WalletFlow.Application.Configuration;
using WalletFlow.Application.Notifications;
using WalletFlow.Application.Support;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.NotificationsAndSupport;
using static WalletFlow.Application.Emails.EmailRequestFields;

namespace WalletFlow.Application.Emails;

// Prépare les e-mails de la file qui portent un contenu lu au moment de l'envoi : une alerte (GR Notif), ou une réponse du
// support (GR Admin). Une alerte ou une réponse effacée depuis la demande ne part plus.
public class AlertAndSupportEmailPreparation
{
    private readonly IUserAccountRepository accountRepository;
    private readonly IAlertRepository alertRepository;
    private readonly IAlertEmails alertEmails;
    private readonly ISupportRequestRepository supportRepository;
    private readonly ISupportEmails supportEmails;
    private readonly PublicWebsiteSettings websiteSettings;

    // Reçoit l'accès aux comptes, aux alertes et au support, les textes des e-mails et l'adresse du site.
    public AlertAndSupportEmailPreparation(
        IUserAccountRepository accountRepository,
        IAlertRepository alertRepository,
        IAlertEmails alertEmails,
        ISupportRequestRepository supportRepository,
        ISupportEmails supportEmails,
        PublicWebsiteSettings websiteSettings)
    {
        this.accountRepository = accountRepository;
        this.alertRepository = alertRepository;
        this.alertEmails = alertEmails;
        this.supportRepository = supportRepository;
        this.supportEmails = supportEmails;
        this.websiteSettings = websiteSettings;
    }

    // Prépare l'e-mail d'une alerte à l'adresse actuelle du compte. Ce qu'il dit est lu au moment de l'envoi : une catégorie
    // ou un sac renommé entre-temps porte son nouveau nom. Un compte effacé, ou une alerte effacée, ne reçoit rien.
    public async Task<EmailToSend> PrepareAlertAsync(EmailRequest request, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.FindByIdAsync(ReadUserId(request), cancellationToken);
        if (account == null)
        {
            return EmailToSend.Nothing(request.UserId);
        }

        var content = await alertRepository.ReadEmailContentAsync(ReadAlertId(request), cancellationToken);
        if (content == null)
        {
            return EmailToSend.Nothing(account.Id);
        }

        return EmailToSend.Send(alertEmails.CreateEmail(account.Email, content), account.Id);
    }

    // Prépare une réponse du support, avec son texte, à l'adresse lue dans sa demande au moment de l'envoi : celle du compte,
    // ou celle du visiteur (GR Admin). Une réponse effacée depuis, avec sa demande, ne part plus.
    public async Task<EmailToSend> PrepareSupportResponseAsync(EmailRequest request, CancellationToken cancellationToken = default)
    {
        var content = await supportRepository.ReadResponseEmailAsync(ReadSupportResponseId(request), cancellationToken);
        if (content == null)
        {
            return EmailToSend.Nothing(null);
        }

        var responseEmail = supportEmails.CreateResponseEmail(
            content.RecipientAddress,
            UsersCalendar.ConvertToUsersTime(content.RequestSentAt),
            content.ResponseText,
            websiteSettings.BuildSupportPageAddress());

        return EmailToSend.Send(responseEmail, content.AccountId);
    }
}
