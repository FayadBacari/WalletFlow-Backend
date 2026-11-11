using WalletFlow.Application.Configuration;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountRecovery;

// Cas d'utilisation : prépare et envoie un e-mail de récupération ou de déblocage, en arrière-plan (GR Auth, SS §2).
public class BackgroundEmailRequestProcessingService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly SecurityJournalService securityJournal;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly IEmailSender emailSender;
    private readonly IAccountRecoveryEmails recoveryEmails;
    private readonly PublicWebsiteSettings websiteSettings;
    private readonly TimeProvider clock;

    // Reçoit les outils dont ces e-mails ont besoin.
    public BackgroundEmailRequestProcessingService(
        IUserAccountRepository accountRepository,
        SecurityJournalService securityJournal,
        ISecureTokenGenerator tokenGenerator,
        IEmailSender emailSender,
        IAccountRecoveryEmails recoveryEmails,
        PublicWebsiteSettings websiteSettings,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.securityJournal = securityJournal;
        this.tokenGenerator = tokenGenerator;
        this.emailSender = emailSender;
        this.recoveryEmails = recoveryEmails;
        this.websiteSettings = websiteSettings;
        this.clock = clock;
    }

    // Traite une demande de la file.
    public async Task<BackgroundEmailRequestResult> ProcessAsync(BackgroundEmailRequest request)
    {
        if (request is BackgroundEmailRequest.PasswordRecovery recoveryRequest)
        {
            return await ProcessRecoveryAsync(recoveryRequest);
        }

        if (request is BackgroundEmailRequest.AccountUnlock unlockRequest)
        {
            return await ProcessUnlockAsync(unlockRequest);
        }

        throw new InvalidOperationException("Cette forme de demande d'e-mail n'est pas prévue.");
    }

    // Choisit l'e-mail selon l'état du compte. Une adresse sans compte ne reçoit rien, et laisse une trace sans identifiant.
    private async Task<BackgroundEmailRequestResult> ProcessRecoveryAsync(BackgroundEmailRequest.PasswordRecovery request)
    {
        var existingAccount = await accountRepository.FindByEmailAsync(request.EmailAddress);

        if (existingAccount == null)
        {
            await RecordRecoveryRequestAsync(SecurityEventResult.Failure, null, request);
            return new BackgroundEmailRequestResult();
        }

        if (!existingAccount.IsEmailConfirmed)
        {
            var invitationEmail = recoveryEmails.CreateSignupInvitationEmail(existingAccount.Email);
            return await SendEmailThenRecordRequestAsync(invitationEmail, SecurityEventResult.Failure, existingAccount, request);
        }

        var recoveryToken = await ReplaceLinkAsync(
            existingAccount.Id,
            EmailTokenType.PasswordRecovery,
            EmailTokenValidityDurations.PasswordRecoveryLink);
        var recoveryEmail = recoveryEmails.CreateRecoveryEmail(
            existingAccount.Email,
            websiteSettings.BuildLink("/reset-password", recoveryToken.Token));

        return await SendEmailThenRecordRequestAsync(recoveryEmail, SecurityEventResult.Success, existingAccount, request);
    }

    // Envoie l'e-mail, puis note la demande au journal. Si l'e-mail ne part pas, la demande est notée comme un échec.
    private async Task<BackgroundEmailRequestResult> SendEmailThenRecordRequestAsync(
        EmailMessage message,
        SecurityEventResult resultIfSent,
        User existingAccount,
        BackgroundEmailRequest request)
    {
        var sendingResult = await TrySendEmailAsync(message, existingAccount.Id);
        var eventResult = sendingResult.EmailCouldNotBeSent ? SecurityEventResult.Failure : resultIfSent;

        await RecordRecoveryRequestAsync(eventResult, existingAccount.Id, request);

        return sendingResult;
    }

    // Envoie le lien de déblocage, sauf si le compte a disparu ou n'est plus bloqué : il n'y a alors rien à débloquer.
    private async Task<BackgroundEmailRequestResult> ProcessUnlockAsync(BackgroundEmailRequest.AccountUnlock request)
    {
        var lockedAccount = await accountRepository.FindByIdAsync(request.AccountId);

        if (lockedAccount == null
            || !lockedAccount.IsLockedAt(clock.GetUtcNow().UtcDateTime))
        {
            return new BackgroundEmailRequestResult();
        }

        var unlockToken = await ReplaceLinkAsync(
            lockedAccount.Id,
            EmailTokenType.AccountUnlock,
            EmailTokenValidityDurations.AccountUnlockLink);
        var unlockEmail = recoveryEmails.CreateUnlockEmail(
            lockedAccount.Email,
            websiteSettings.BuildLink("/unlock-account", unlockToken.Token),
            AccountLockoutRules.CalculateLockoutDuration(lockedAccount.LockoutCount));

        return await TrySendEmailAsync(unlockEmail, lockedAccount.Id);
    }

    // Tire un nouveau jeton et remplace l'ancien lien du même type, qui ne marche plus (SS §2). Seule l'empreinte va en base.
    private async Task<GeneratedSecureToken> ReplaceLinkAsync(Guid accountId, EmailTokenType linkType, TimeSpan validity)
    {
        var linkToken = tokenGenerator.GenerateToken();

        await accountRepository.ReplaceTokenAsync(new EmailToken
        {
            Type = linkType,
            TokenHash = linkToken.Hash,
            ExpiresAt = clock.GetUtcNow().UtcDateTime + validity,
            UserId = accountId
        });

        return linkToken;
    }

    // Envoie un e-mail. S'il ne peut pas partir, la panne ira au journal technique : l'annoncer au visiteur révélerait le compte (SS §2).
    private async Task<BackgroundEmailRequestResult> TrySendEmailAsync(EmailMessage message, Guid accountId)
    {
        try
        {
            await emailSender.SendAsync(message);
            return new BackgroundEmailRequestResult();
        }
        catch (EmailCouldNotBeSentException)
        {
            return new BackgroundEmailRequestResult { EmailCouldNotBeSent = true, UserId = accountId };
        }
    }

    // Écrit la demande de récupération dans le journal de sécurité, sans jamais l'adresse e-mail (SS §7 et §11).
    private async Task RecordRecoveryRequestAsync(SecurityEventResult eventResult, Guid? accountId, BackgroundEmailRequest request)
    {
        await securityJournal.SaveEventNowAsync(
            SecurityEventType.PasswordRecovery,
            eventResult,
            accountId,
            request.IpAddress);
    }
}
