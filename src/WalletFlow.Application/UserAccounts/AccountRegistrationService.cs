using WalletFlow.Application.Configuration;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserAccounts;

// Cas d'utilisation : un visiteur crée un compte (GR Auth, SS §2).
public class AccountRegistrationService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly IDatabaseUnitOfWork unitOfWork;
    private readonly IPasswordHasher passwordHasher;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly IEmailSender emailSender;
    private readonly IAccountRegistrationEmails registrationEmails;
    private readonly PublicWebsiteSettings websiteSettings;
    private readonly IEmailAddressRequestLimiter requestLimiter;
    private readonly TimeProvider clock;

    // Reçoit les outils dont l'inscription a besoin.
    public AccountRegistrationService(
        IUserAccountRepository accountRepository,
        IDatabaseUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ISecureTokenGenerator tokenGenerator,
        IEmailSender emailSender,
        IAccountRegistrationEmails registrationEmails,
        PublicWebsiteSettings websiteSettings,
        IEmailAddressRequestLimiter requestLimiter,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.unitOfWork = unitOfWork;
        this.passwordHasher = passwordHasher;
        this.tokenGenerator = tokenGenerator;
        this.emailSender = emailSender;
        this.registrationEmails = registrationEmails;
        this.websiteSettings = websiteSettings;
        this.requestLimiter = requestLimiter;
        this.clock = clock;
    }

    // Inscrit un visiteur. La réponse est la même que l'adresse soit libre, non confirmée ou confirmée,
    // pour ne pas révéler quels comptes existent : seul l'e-mail envoyé change (SS §2).
    public async Task<AccountRegistrationResult> RegisterAsync(
        string typedEmail,
        string password)
    {
        var email = EmailAddressRules.Normalize(typedEmail);
        var normalizedPassword = PasswordRules.Normalize(password);

        var refusal = FindRuleRefusal(email, normalizedPassword);
        if (refusal != null)
        {
            return refusal;
        }

        // La limite compte les demandes, qu'un compte existe ou non : elle ne révèle donc rien (SS §6).
        if (!requestLimiter.TryCountRequest(email))
        {
            return new AccountRegistrationResult.TooManyRequests();
        }

        // Le mot de passe est haché dans tous les cas : sinon, une réponse plus rapide
        // révélerait que l'adresse est déjà utilisée.
        var passwordHash = await passwordHasher.HashAsync(normalizedPassword);

        return await SaveAccountThenSendEmailAsync(email, passwordHash);
    }

    // Enregistre le compte selon l'état de l'adresse, puis envoie le bon e-mail.
    private async Task<AccountRegistrationResult> SaveAccountThenSendEmailAsync(
        string email,
        string passwordHash)
    {
        var existingAccount = await accountRepository.FindByEmailAsync(email);

        if (existingAccount == null)
        {
            var result = await TrySaveNewAccountThenSendLinkAsync(email, passwordHash);
            if (result != null)
            {
                return result;
            }

            // Une autre inscription vient de prendre la même adresse.
            existingAccount = await accountRepository.FindByEmailAsync(email);
        }

        if (existingAccount != null && !existingAccount.IsEmailConfirmed)
        {
            var result = await TryReplacePasswordThenSendLinkAsync(existingAccount, passwordHash);
            if (result != null)
            {
                return result;
            }
        }

        // L'adresse est confirmée, ou vient de l'être pendant cette inscription : son mot de passe ne change pas.
        var warningEmail = registrationEmails.CreateAddressAlreadyUsedEmail(email);
        return await SendEmailAsync(warningEmail, existingAccount?.Id);
    }

    // Enregistre un nouveau compte et envoie son lien. Renvoie null si une autre inscription vient de prendre l'adresse.
    private async Task<AccountRegistrationResult?> TrySaveNewAccountThenSendLinkAsync(
        string email,
        string passwordHash)
    {
        var confirmationToken = tokenGenerator.GenerateToken();
        var newAccount = new User { Email = email, PasswordHash = passwordHash };

        var saved = await accountRepository.TryAddAccountAsync(
            newAccount,
            DefaultCategories.CreateForUser(newAccount.Id),
            CreateConfirmationToken(confirmationToken, newAccount.Id));

        if (!saved)
        {
            return null;
        }

        return await SendConfirmationEmailAsync(email, confirmationToken, newAccount.Id);
    }

    // Remplace le mot de passe d'un compte non confirmé et envoie un nouveau lien (GR Auth).
    // Renvoie null si le compte vient d'être confirmé ou effacé : rien n'a alors changé.
    private async Task<AccountRegistrationResult?> TryReplacePasswordThenSendLinkAsync(
        User account,
        string passwordHash)
    {
        var confirmationToken = tokenGenerator.GenerateToken();

        account.ReplacePasswordHash(passwordHash);
        await accountRepository.PrepareConfirmationTokenReplacementAsync(
            CreateConfirmationToken(confirmationToken, account.Id));

        // L'enregistrement contrôle que le compte n'est toujours pas confirmé : le mot de passe d'un compte déjà actif n'est jamais remplacé.
        var saved = await unitOfWork.TrySaveChangesAsync();

        if (!saved)
        {
            return null;
        }

        return await SendConfirmationEmailAsync(account.Email, confirmationToken, account.Id);
    }

    // Vérifie le format de l'adresse et les règles du mot de passe, côté serveur (SS §5).
    // Renvoie null si la demande respecte toutes les règles.
    private static AccountRegistrationResult.RefusedByRules? FindRuleRefusal(
        string email,
        string normalizedPassword)
    {
        var isEmailInvalid = !EmailAddressRules.IsValid(email);
        var isPasswordInvalid = !PasswordRules.IsValid(normalizedPassword);

        if (!isEmailInvalid && !isPasswordInvalid)
        {
            return null;
        }

        return new AccountRegistrationResult.RefusedByRules
        {
            IsEmailInvalid = isEmailInvalid,
            IsPasswordInvalid = isPasswordInvalid
        };
    }

    // Crée le jeton de confirmation d'un compte : seule son empreinte ira en base (SS §2).
    private EmailToken CreateConfirmationToken(GeneratedSecureToken confirmationToken, Guid accountId)
    {
        return new EmailToken
        {
            Type = EmailTokenType.SignupConfirmation,
            TokenHash = confirmationToken.Hash,
            ExpiresAt = clock.GetUtcNow().UtcDateTime + EmailTokenValidityDurations.SignupConfirmationLink,
            UserId = accountId
        };
    }

    // Envoie au visiteur l'e-mail qui contient le lien de confirmation.
    private async Task<AccountRegistrationResult> SendConfirmationEmailAsync(
        string email,
        GeneratedSecureToken confirmationToken,
        Guid accountId)
    {
        var confirmationEmail = registrationEmails.CreateConfirmationEmail(
            email,
            websiteSettings.BuildLink("/confirm-email", confirmationToken.Token));

        return await SendEmailAsync(confirmationEmail, accountId);
    }

    // Envoie un e-mail. S'il ne peut pas partir, le visiteur est invité à réessayer :
    // le compte reste non confirmé, et une nouvelle inscription enverra un nouveau lien (GR Auth).
    private async Task<AccountRegistrationResult> SendEmailAsync(EmailMessage message, Guid? accountId)
    {
        try
        {
            await emailSender.SendAsync(message);
            return new AccountRegistrationResult.Accepted();
        }
        catch (EmailCouldNotBeSentException)
        {
            return new AccountRegistrationResult.EmailCouldNotBeSent { UserId = accountId };
        }
    }
}
