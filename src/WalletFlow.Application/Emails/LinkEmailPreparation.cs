using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.AccountSettings;
using WalletFlow.Application.Configuration;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Application.Emails.EmailRequestFields;

namespace WalletFlow.Application.Emails;

// Prépare les e-mails de la file qui portent un lien à usage unique : récupération du mot de passe, déblocage, changement
// d'adresse et désactivation de la double authentification (SS §2 et §9). Le lien est créé au moment de l'essai : la file
// n'en garde aucun, et le lien d'un nouvel essai remplace celui de l'essai raté.
public class LinkEmailPreparation
{
    private readonly IUserAccountRepository accountRepository;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly IAccountRecoveryEmails recoveryEmails;
    private readonly IAccountChangeEmails changeEmails;
    private readonly PublicWebsiteSettings websiteSettings;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux comptes, le créateur de jetons, les textes des e-mails, l'adresse du site et l'horloge.
    public LinkEmailPreparation(
        IUserAccountRepository accountRepository,
        ISecureTokenGenerator tokenGenerator,
        IAccountRecoveryEmails recoveryEmails,
        IAccountChangeEmails changeEmails,
        PublicWebsiteSettings websiteSettings,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.tokenGenerator = tokenGenerator;
        this.recoveryEmails = recoveryEmails;
        this.changeEmails = changeEmails;
        this.websiteSettings = websiteSettings;
        this.clock = clock;
    }

    // Choisit l'e-mail selon l'état du compte. Une adresse sans compte ne reçoit rien, et laisse une trace sans identifiant.
    // Une adresse non confirmée reçoit une invitation à s'inscrire à nouveau (GR Auth), une adresse confirmée le lien.
    public async Task<EmailToSend> PrepareRecoveryAsync(EmailRequest request, CancellationToken cancellationToken = default)
    {
        var existingAccount = await accountRepository.FindByEmailAsync(ReadEmailAddress(request), cancellationToken);
        if (existingAccount == null)
        {
            return EmailToSend.Nothing(null, SecurityEventResult.Failure);
        }

        if (!existingAccount.IsEmailConfirmed)
        {
            var invitationEmail = recoveryEmails.CreateSignupInvitationEmail(existingAccount.Email);
            return EmailToSend.Send(invitationEmail, existingAccount.Id, SecurityEventResult.Failure);
        }

        var recoveryToken = await ReplaceLinkAsync(
            existingAccount.Id,
            EmailTokenType.PasswordRecovery,
            EmailTokenValidityDurations.PasswordRecoveryLink,
            null,
            cancellationToken);
        var recoveryEmail = recoveryEmails.CreateRecoveryEmail(
            existingAccount.Email,
            websiteSettings.BuildLink("/reset-password", recoveryToken.Token));

        return EmailToSend.Send(recoveryEmail, existingAccount.Id, SecurityEventResult.Success);
    }

    // Prépare le lien de déblocage, sauf si le compte a disparu ou n'est plus bloqué : il n'y a alors rien à débloquer.
    public async Task<EmailToSend> PrepareUnlockAsync(EmailRequest request, CancellationToken cancellationToken = default)
    {
        var lockedAccount = await accountRepository.FindByIdAsync(ReadUserId(request), cancellationToken);
        if (lockedAccount == null
            || !lockedAccount.IsLockedAt(clock.GetUtcNow().UtcDateTime))
        {
            return EmailToSend.Nothing(request.UserId);
        }

        var unlockToken = await ReplaceLinkAsync(
            lockedAccount.Id,
            EmailTokenType.AccountUnlock,
            EmailTokenValidityDurations.AccountUnlockLink,
            null,
            cancellationToken);
        var unlockEmail = recoveryEmails.CreateUnlockEmail(
            lockedAccount.Email,
            websiteSettings.BuildLink("/unlock-account", unlockToken.Token),
            AccountLockoutRules.CalculateLockoutDuration(lockedAccount.LockoutCount));

        return EmailToSend.Send(unlockEmail, lockedAccount.Id);
    }

    // Une adresse libre reçoit le lien qui la confirme. Une adresse déjà utilisée ne reçoit pas de lien : son propriétaire est prévenu.
    // L'utilisateur a déjà reçu la même réponse dans les deux cas (SS §2). Un compte effacé depuis sa demande ne reçoit rien.
    public async Task<EmailToSend> PrepareEmailChangeAsync(EmailRequest request, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.FindByIdAsync(ReadUserId(request), cancellationToken);
        if (account == null)
        {
            return EmailToSend.Nothing(request.UserId);
        }

        var newEmail = ReadEmailAddress(request);
        if (await accountRepository.FindByEmailAsync(newEmail, cancellationToken) != null)
        {
            return EmailToSend.Send(changeEmails.CreateAddressAlreadyUsedEmail(newEmail), account.Id);
        }

        var linkToken = await ReplaceLinkAsync(
            account.Id,
            EmailTokenType.EmailChange,
            EmailTokenValidityDurations.EmailChangeLink,
            newEmail,
            cancellationToken);
        var linkEmail = changeEmails.CreateEmailChangeLinkEmail(
            newEmail,
            websiteSettings.BuildLink("/confirm-email-change", linkToken.Token));

        return EmailToSend.Send(linkEmail, account.Id);
    }

    // Prépare le lien du support qui désactive la double authentification, à l'adresse actuelle du compte. Un compte effacé,
    // ou dont la double authentification est déjà désactivée, ne reçoit rien : il n'y a plus rien à désactiver.
    public async Task<EmailToSend> PrepareTwoFactorLinkAsync(EmailRequest request, CancellationToken cancellationToken = default)
    {
        var account = await accountRepository.FindByIdAsync(ReadUserId(request), cancellationToken);
        if (account == null || !account.IsTwoFactorEnabled)
        {
            return EmailToSend.Nothing(request.UserId);
        }

        var linkToken = await ReplaceLinkAsync(
            account.Id,
            EmailTokenType.TwoFactorDeactivation,
            EmailTokenValidityDurations.TwoFactorDeactivationLink,
            null,
            cancellationToken);
        var linkEmail = recoveryEmails.CreateTwoFactorDeactivationLinkEmail(
            account.Email,
            websiteSettings.BuildLink("/disable-two-factor", linkToken.Token),
            websiteSettings.BuildSupportPageAddress());

        return EmailToSend.Send(linkEmail, account.Id);
    }

    // Tire un nouveau jeton et remplace l'ancien lien du même type, qui ne marche plus (SS §2). Seule l'empreinte va en base.
    // La nouvelle adresse n'accompagne que le lien d'un changement d'adresse : sinon, elle est vide (DD §4.2).
    private async Task<GeneratedSecureToken> ReplaceLinkAsync(Guid accountId, EmailTokenType linkType, TimeSpan validity, string? newEmail, CancellationToken cancellationToken = default)
    {
        var linkToken = tokenGenerator.GenerateToken();

        await accountRepository.ReplaceTokenAsync(new EmailToken
        {
            Type = linkType,
            TokenHash = linkToken.Hash,
            ExpiresAt = clock.GetUtcNow().UtcDateTime + validity,
            NewEmail = newEmail,
            UserId = accountId
        },
        cancellationToken);

        return linkToken;
    }
}
