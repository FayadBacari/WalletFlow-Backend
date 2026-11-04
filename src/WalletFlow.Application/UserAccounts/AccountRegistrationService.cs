using WalletFlow.Application.Configuration;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Security;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserAccounts;

// Cas d'utilisation : un visiteur crée un compte (GR Auth, SS §2).
public class AccountRegistrationService
{
    public const string ErrorMessageForInvalidEmailAddressFormat = "L'adresse e-mail n'est pas valide.";

    public const string ErrorMessageForPasswordBreakingTheRules =
        "Le mot de passe doit contenir de 12 à 128 caractères, dont une majuscule, une minuscule, un chiffre et un caractère spécial, sans caractère invisible.";

    private readonly IUserAccountRepository repositoryOfTheUserAccountsInDatabase;
    private readonly IPasswordHasher hasherOfThePasswordsChosenByVisitors;
    private readonly ISecureTokenGenerator generatorOfTheSecureRandomEmailTokens;
    private readonly IEmailSender senderOfTheEmailsToTheVisitors;
    private readonly PublicWebsiteSettings settingsOfThePublicWalletFlowWebsite;
    private readonly IEmailAddressRequestLimiter limiterOfTheRequestsPerEmailAddress;
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;

    // Reçoit les outils dont l'inscription a besoin.
    public AccountRegistrationService(
        IUserAccountRepository repositoryOfTheUserAccountsInDatabase,
        IPasswordHasher hasherOfThePasswordsChosenByVisitors,
        ISecureTokenGenerator generatorOfTheSecureRandomEmailTokens,
        IEmailSender senderOfTheEmailsToTheVisitors,
        PublicWebsiteSettings settingsOfThePublicWalletFlowWebsite,
        IEmailAddressRequestLimiter limiterOfTheRequestsPerEmailAddress,
        TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.repositoryOfTheUserAccountsInDatabase = repositoryOfTheUserAccountsInDatabase;
        this.hasherOfThePasswordsChosenByVisitors = hasherOfThePasswordsChosenByVisitors;
        this.generatorOfTheSecureRandomEmailTokens = generatorOfTheSecureRandomEmailTokens;
        this.senderOfTheEmailsToTheVisitors = senderOfTheEmailsToTheVisitors;
        this.settingsOfThePublicWalletFlowWebsite = settingsOfThePublicWalletFlowWebsite;
        this.limiterOfTheRequestsPerEmailAddress = limiterOfTheRequestsPerEmailAddress;
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Inscrit un visiteur. La réponse est la même que l'adresse soit libre, non confirmée ou confirmée,
    // pour ne pas révéler quels comptes existent : seul l'e-mail envoyé change (SS §2).
    public async Task<AccountRegistrationResult> RegisterNewAccountForVisitorAsync(
        string emailAddressTypedByTheVisitor,
        string passwordChosenByTheVisitorToRegister)
    {
        var emailAddressInItsStoredForm = EmailAddressRules.ConvertEmailAddressIntoItsStoredForm(emailAddressTypedByTheVisitor);
        var passwordWrittenInItsNormalizedForm = PasswordRules.ConvertPasswordIntoItsNormalizedForm(passwordChosenByTheVisitorToRegister);

        var resultOfTheVisitorRequestValidation = CheckThatVisitorRequestRespectsTheRules(emailAddressInItsStoredForm, passwordWrittenInItsNormalizedForm);
        if (!resultOfTheVisitorRequestValidation.IsAccepted)
        {
            return resultOfTheVisitorRequestValidation;
        }

        // La limite compte les demandes, qu'un compte existe ou non : elle ne révèle donc rien (SS §6).
        if (!limiterOfTheRequestsPerEmailAddress.TryToCountNewRequestForThisEmailAddress(emailAddressInItsStoredForm))
        {
            return new AccountRegistrationResult { TooManyRequestsForThisEmailAddress = true };
        }

        // Le mot de passe est haché dans tous les cas : sinon, une réponse plus rapide
        // révélerait que l'adresse est déjà utilisée.
        var secureHashOfTheChosenPassword = hasherOfThePasswordsChosenByVisitors.ConvertPasswordIntoItsSecureHash(passwordWrittenInItsNormalizedForm);

        return await SaveAccountThenSendEmailToTheVisitorAsync(emailAddressInItsStoredForm, secureHashOfTheChosenPassword);
    }

    // Enregistre le compte selon l'état de l'adresse, puis envoie le bon e-mail.
    private async Task<AccountRegistrationResult> SaveAccountThenSendEmailToTheVisitorAsync(
        string emailAddressInItsStoredForm,
        string secureHashOfTheChosenPassword)
    {
        var accountAlreadyUsingThisEmailAddress = await repositoryOfTheUserAccountsInDatabase.FindUserAccountByEmailAddressAsync(emailAddressInItsStoredForm);

        if (accountAlreadyUsingThisEmailAddress == null)
        {
            var resultForTheBrandNewAccount = await TryToSaveBrandNewAccountThenSendItsLinkAsync(emailAddressInItsStoredForm, secureHashOfTheChosenPassword);
            if (resultForTheBrandNewAccount != null)
            {
                return resultForTheBrandNewAccount;
            }

            // Une autre inscription vient de prendre la même adresse.
            accountAlreadyUsingThisEmailAddress = await repositoryOfTheUserAccountsInDatabase.FindUserAccountByEmailAddressAsync(emailAddressInItsStoredForm);
        }

        if (accountAlreadyUsingThisEmailAddress != null && !accountAlreadyUsingThisEmailAddress.IsEmailConfirmed)
        {
            var resultForTheAccountNotYetConfirmed = await TryToReplacePasswordThenSendNewLinkAsync(accountAlreadyUsingThisEmailAddress, secureHashOfTheChosenPassword);
            if (resultForTheAccountNotYetConfirmed != null)
            {
                return resultForTheAccountNotYetConfirmed;
            }
        }

        // L'adresse est confirmée, ou vient de l'être pendant cette inscription : son mot de passe ne change pas.
        var warningEmailForTheAddressOwner = AccountRegistrationEmails.CreateWarningEmailForAddressAlreadyUsed(emailAddressInItsStoredForm);
        return await SendEmailToTheVisitorAsync(warningEmailForTheAddressOwner, accountAlreadyUsingThisEmailAddress?.Id);
    }

    // Enregistre un nouveau compte et envoie son lien. Renvoie null si une autre inscription vient de prendre l'adresse.
    private async Task<AccountRegistrationResult?> TryToSaveBrandNewAccountThenSendItsLinkAsync(
        string emailAddressInItsStoredForm,
        string secureHashOfTheChosenPassword)
    {
        var generatedTokenForTheConfirmationLink = generatorOfTheSecureRandomEmailTokens.GenerateNewRandomSecureTokenWithItsHash();
        var newUserToRegisterInTheDatabase = new User { Email = emailAddressInItsStoredForm, PasswordHash = secureHashOfTheChosenPassword };

        var newAccountWasSavedInTheDatabase = await repositoryOfTheUserAccountsInDatabase.TryToSaveNewAccountWithCategoriesAndTokenAsync(
            newUserToRegisterInTheDatabase,
            DefaultCategories.CreateTheEightDefaultCategoriesForNewUser(newUserToRegisterInTheDatabase.Id),
            CreateSignupConfirmationTokenForThisAccount(generatedTokenForTheConfirmationLink, newUserToRegisterInTheDatabase.Id));

        if (!newAccountWasSavedInTheDatabase)
        {
            return null;
        }

        return await SendSignupConfirmationEmailToTheVisitorAsync(emailAddressInItsStoredForm, generatedTokenForTheConfirmationLink, newUserToRegisterInTheDatabase.Id);
    }

    // Remplace le mot de passe d'un compte non confirmé et envoie un nouveau lien (GR Auth).
    // Renvoie null si le compte vient d'être confirmé ou effacé : rien n'a alors changé.
    private async Task<AccountRegistrationResult?> TryToReplacePasswordThenSendNewLinkAsync(
        User unconfirmedAccountToUpdateInTheDatabase,
        string secureHashOfTheChosenPassword)
    {
        var generatedTokenForTheConfirmationLink = generatorOfTheSecureRandomEmailTokens.GenerateNewRandomSecureTokenWithItsHash();

        var unconfirmedAccountWasUpdatedInTheDatabase = await repositoryOfTheUserAccountsInDatabase.TryToReplacePasswordAndTokenOfUnconfirmedAccountAsync(
            unconfirmedAccountToUpdateInTheDatabase,
            secureHashOfTheChosenPassword,
            CreateSignupConfirmationTokenForThisAccount(generatedTokenForTheConfirmationLink, unconfirmedAccountToUpdateInTheDatabase.Id));

        if (!unconfirmedAccountWasUpdatedInTheDatabase)
        {
            return null;
        }

        return await SendSignupConfirmationEmailToTheVisitorAsync(unconfirmedAccountToUpdateInTheDatabase.Email, generatedTokenForTheConfirmationLink, unconfirmedAccountToUpdateInTheDatabase.Id);
    }

    // Vérifie le format de l'adresse et les règles du mot de passe, côté serveur (SS §5).
    private static AccountRegistrationResult CheckThatVisitorRequestRespectsTheRules(
        string emailAddressInItsStoredForm,
        string passwordWrittenInItsNormalizedForm)
    {
        string? errorMessageAboutTheEmailAddressFormat = null;
        string? errorMessageAboutTheChosenPassword = null;

        if (!EmailAddressRules.CheckThatEmailAddressHasValidFormat(emailAddressInItsStoredForm))
        {
            errorMessageAboutTheEmailAddressFormat = ErrorMessageForInvalidEmailAddressFormat;
        }

        if (!PasswordRules.CheckThatPasswordRespectsAllTheRules(passwordWrittenInItsNormalizedForm))
        {
            errorMessageAboutTheChosenPassword = ErrorMessageForPasswordBreakingTheRules;
        }

        return new AccountRegistrationResult
        {
            EmailAddressErrorMessage = errorMessageAboutTheEmailAddressFormat,
            PasswordErrorMessage = errorMessageAboutTheChosenPassword
        };
    }

    // Crée le jeton de confirmation d'un compte : seule son empreinte ira en base (SS §2).
    private EmailToken CreateSignupConfirmationTokenForThisAccount(GeneratedSecureToken generatedTokenForTheConfirmationLink, Guid identifierOfTheAccountToConfirm)
    {
        return new EmailToken
        {
            Type = EmailTokenType.SignupConfirmation,
            TokenHash = generatedTokenForTheConfirmationLink.HashToStoreInTheDatabase,
            ExpiresAt = clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime + EmailTokenValidityDurations.ValidityDurationOfTheSignupConfirmationLink,
            UserId = identifierOfTheAccountToConfirm
        };
    }

    // Envoie au visiteur l'e-mail qui contient le lien de confirmation.
    private async Task<AccountRegistrationResult> SendSignupConfirmationEmailToTheVisitorAsync(
        string emailAddressInItsStoredForm,
        GeneratedSecureToken generatedTokenForTheConfirmationLink,
        Guid identifierOfTheAccountToConfirm)
    {
        var emailWithTheSignupConfirmationLink = AccountRegistrationEmails.CreateSignupConfirmationEmailWithItsLink(
            emailAddressInItsStoredForm,
            BuildSignupConfirmationLinkForEmailToken(generatedTokenForTheConfirmationLink.TokenGivenOnlyToTheVisitor));

        return await SendEmailToTheVisitorAsync(emailWithTheSignupConfirmationLink, identifierOfTheAccountToConfirm);
    }

    // Envoie un e-mail. S'il ne peut pas partir, le visiteur est invité à réessayer :
    // le compte reste non confirmé, et une nouvelle inscription enverra un nouveau lien (GR Auth).
    private async Task<AccountRegistrationResult> SendEmailToTheVisitorAsync(EmailMessageToSend emailMessageToSendToTheVisitor, Guid? identifierOfTheAccountConcernedByTheEmail)
    {
        try
        {
            await senderOfTheEmailsToTheVisitors.SendEmailToItsRecipientAsync(emailMessageToSendToTheVisitor);
            return new AccountRegistrationResult();
        }
        catch (EmailCouldNotBeSentException)
        {
            return new AccountRegistrationResult
            {
                EmailCouldNotBeSent = true,
                UserIdentifierForTheTechnicalLog = identifierOfTheAccountConcernedByTheEmail
            };
        }
    }

    // Construit le lien de confirmation. Le lien ouvre une page du site, qui demande le mot de passe
    // avant de confirmer (SS §2). Le jeton est placé après le # : il n'est jamais envoyé au serveur
    // quand la page s'ouvre, et n'apparaît donc pas dans ses journaux (SS §7).
    private string BuildSignupConfirmationLinkForEmailToken(string tokenToSendInTheEmailLink)
    {
        return settingsOfThePublicWalletFlowWebsite.PublicWebsiteAddress + "/confirm-email#token=" + tokenToSendInTheEmailLink;
    }
}
