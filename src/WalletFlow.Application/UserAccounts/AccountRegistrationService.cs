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

    // Reçoit les outils dont l'inscription a besoin.
    public AccountRegistrationService(
        IUserAccountRepository repositoryOfTheUserAccountsInDatabase,
        IPasswordHasher hasherOfThePasswordsChosenByVisitors,
        ISecureTokenGenerator generatorOfTheSecureRandomEmailTokens,
        IEmailSender senderOfTheEmailsToTheVisitors,
        PublicWebsiteSettings settingsOfThePublicWalletFlowWebsite)
    {
        this.repositoryOfTheUserAccountsInDatabase = repositoryOfTheUserAccountsInDatabase;
        this.hasherOfThePasswordsChosenByVisitors = hasherOfThePasswordsChosenByVisitors;
        this.generatorOfTheSecureRandomEmailTokens = generatorOfTheSecureRandomEmailTokens;
        this.senderOfTheEmailsToTheVisitors = senderOfTheEmailsToTheVisitors;
        this.settingsOfThePublicWalletFlowWebsite = settingsOfThePublicWalletFlowWebsite;
    }

    // Inscrit un visiteur. La réponse est la même que l'adresse soit libre ou déjà utilisée,
    // pour ne pas révéler quels comptes existent : seul l'e-mail envoyé change (SS §2).
    public async Task<AccountRegistrationResult> RegisterNewAccountForVisitorAsync(
        string emailAddressTypedByTheVisitor,
        string passwordChosenByTheVisitorToRegister)
    {
        var emailAddressInItsStoredForm = EmailAddressRules.ConvertEmailAddressIntoItsStoredForm(emailAddressTypedByTheVisitor);

        var resultOfTheVisitorRequestValidation = CheckThatVisitorRequestRespectsTheRules(emailAddressInItsStoredForm, passwordChosenByTheVisitorToRegister);
        if (!resultOfTheVisitorRequestValidation.IsAccepted)
        {
            return resultOfTheVisitorRequestValidation;
        }

        // Le mot de passe est haché dans tous les cas : sinon, une réponse plus rapide
        // révélerait que l'adresse est déjà utilisée.
        var secureHashOfTheChosenPassword = hasherOfThePasswordsChosenByVisitors.ConvertPasswordIntoItsSecureHash(passwordChosenByTheVisitorToRegister);

        var emailAddressIsAlreadyUsedByAccount = await repositoryOfTheUserAccountsInDatabase.CheckThatEmailAddressIsAlreadyUsedAsync(emailAddressInItsStoredForm);
        if (emailAddressIsAlreadyUsedByAccount)
        {
            await SendWarningToOwnerOfTheUsedAddressAsync(emailAddressInItsStoredForm);
            return new AccountRegistrationResult();
        }

        var newUserToRegisterInTheDatabase = new User
        {
            Email = emailAddressInItsStoredForm,
            PasswordHash = secureHashOfTheChosenPassword
        };
        var defaultCategoriesOfTheNewUser = DefaultCategories.CreateTheEightDefaultCategoriesForNewUser(newUserToRegisterInTheDatabase.Id);
        var generatedTokenForTheConfirmationLink = generatorOfTheSecureRandomEmailTokens.GenerateNewRandomSecureTokenWithItsHash();
        var signupConfirmationTokenOfTheNewUser = new EmailToken
        {
            Type = EmailTokenType.SignupConfirmation,
            TokenHash = generatedTokenForTheConfirmationLink.HashToStoreInTheDatabase,
            ExpiresAt = DateTime.UtcNow + EmailTokenValidityDurations.ValidityDurationOfTheSignupConfirmationLink,
            UserId = newUserToRegisterInTheDatabase.Id
        };

        var newAccountWasSavedInTheDatabase = await repositoryOfTheUserAccountsInDatabase.TryToSaveNewAccountWithCategoriesAndTokenAsync(
            newUserToRegisterInTheDatabase,
            defaultCategoriesOfTheNewUser,
            signupConfirmationTokenOfTheNewUser);

        // Une autre inscription a pu prendre la même adresse juste après la vérification ci-dessus.
        if (!newAccountWasSavedInTheDatabase)
        {
            await SendWarningToOwnerOfTheUsedAddressAsync(emailAddressInItsStoredForm);
            return new AccountRegistrationResult();
        }

        var emailWithTheSignupConfirmationLink = AccountRegistrationEmails.CreateSignupConfirmationEmailWithItsLink(
            emailAddressInItsStoredForm,
            BuildSignupConfirmationLinkForEmailToken(generatedTokenForTheConfirmationLink.TokenToSendInTheEmailLink));
        await senderOfTheEmailsToTheVisitors.SendEmailToItsRecipientAsync(emailWithTheSignupConfirmationLink);

        return new AccountRegistrationResult();
    }

    // Vérifie le format de l'adresse et les règles du mot de passe, côté serveur (SS §5).
    private static AccountRegistrationResult CheckThatVisitorRequestRespectsTheRules(
        string emailAddressInItsStoredForm,
        string passwordChosenByTheVisitorToRegister)
    {
        string? errorMessageAboutTheEmailAddressFormat = null;
        string? errorMessageAboutTheChosenPassword = null;

        if (!EmailAddressRules.CheckThatEmailAddressHasValidFormat(emailAddressInItsStoredForm))
        {
            errorMessageAboutTheEmailAddressFormat = ErrorMessageForInvalidEmailAddressFormat;
        }

        if (!PasswordRules.CheckThatPasswordRespectsAllTheRules(passwordChosenByTheVisitorToRegister))
        {
            errorMessageAboutTheChosenPassword = ErrorMessageForPasswordBreakingTheRules;
        }

        return new AccountRegistrationResult
        {
            EmailAddressErrorMessage = errorMessageAboutTheEmailAddressFormat,
            PasswordErrorMessage = errorMessageAboutTheChosenPassword
        };
    }

    // Prévient le propriétaire de l'adresse qu'une inscription a été tentée avec elle (SS §2).
    private async Task SendWarningToOwnerOfTheUsedAddressAsync(string emailAddressInItsStoredForm)
    {
        var warningEmailForTheAddressOwner = AccountRegistrationEmails.CreateWarningEmailForAddressAlreadyUsed(emailAddressInItsStoredForm);
        await senderOfTheEmailsToTheVisitors.SendEmailToItsRecipientAsync(warningEmailForTheAddressOwner);
    }

    // Construit le lien de confirmation. Le lien ouvre une page du site, qui demande un clic avant
    // de confirmer (SS §2). Le jeton est placé après le # : il n'est jamais envoyé au serveur
    // quand la page s'ouvre, et n'apparaît donc pas dans ses journaux (SS §7).
    private string BuildSignupConfirmationLinkForEmailToken(string tokenToSendInTheEmailLink)
    {
        return settingsOfThePublicWalletFlowWebsite.PublicWebsiteAddress + "/confirm-email#token=" + tokenToSendInTheEmailLink;
    }
}
