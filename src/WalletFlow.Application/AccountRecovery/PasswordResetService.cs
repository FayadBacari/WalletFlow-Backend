using System.Net;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountRecovery;

// Cas d'utilisation : un visiteur choisit un nouveau mot de passe avec le lien reçu par e-mail (GR Auth, SS §2 et §3).
// Le lien a prouvé qu'il possède la boîte mail : le compte est aussi débloqué. Il ne connecte personne.
public class PasswordResetService
{
    public const string ErrorMessageForRecoveryLinkNoLongerValid = "Ce lien n'est plus valable. Demande un nouveau lien avec « Mot de passe oublié ».";

    private readonly IUserAccountRepository repositoryOfTheUserAccountsInDatabase;
    private readonly IUserSessionRepository repositoryOfTheUserSessionsInDatabase;
    private readonly IPasswordHasher hasherOfThePasswordsChosenByVisitors;
    private readonly ISecureTokenGenerator generatorOfTheSecureRandomTokens;
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;

    // Reçoit les outils dont le changement de mot de passe a besoin.
    public PasswordResetService(
        IUserAccountRepository repositoryOfTheUserAccountsInDatabase,
        IUserSessionRepository repositoryOfTheUserSessionsInDatabase,
        IPasswordHasher hasherOfThePasswordsChosenByVisitors,
        ISecureTokenGenerator generatorOfTheSecureRandomTokens,
        TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.repositoryOfTheUserAccountsInDatabase = repositoryOfTheUserAccountsInDatabase;
        this.repositoryOfTheUserSessionsInDatabase = repositoryOfTheUserSessionsInDatabase;
        this.hasherOfThePasswordsChosenByVisitors = hasherOfThePasswordsChosenByVisitors;
        this.generatorOfTheSecureRandomTokens = generatorOfTheSecureRandomTokens;
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Change le mot de passe si le lien est encore valable. Un mot de passe refusé par les règles ne consomme pas le lien.
    public async Task<PasswordResetResult> ResetPasswordWithLinkTokenAsync(string tokenReadInTheRecoveryLink, string newPasswordTypedByTheVisitor, IPAddress? ipAddressOfTheRequestingVisitor)
    {
        var hashOfTheTokenFromTheLink = generatorOfTheSecureRandomTokens.ConvertSecureTokenIntoItsHash(tokenReadInTheRecoveryLink);

        if (await FindRecoveryTokenThatCanStillBeUsedAsync(hashOfTheTokenFromTheLink) == null)
        {
            return CreateResultForTheLinkNoLongerValid();
        }

        var passwordWrittenInItsNormalizedForm = PasswordRules.ConvertPasswordIntoItsNormalizedForm(newPasswordTypedByTheVisitor);

        if (!PasswordRules.CheckThatPasswordRespectsAllTheRules(passwordWrittenInItsNormalizedForm))
        {
            return new PasswordResetResult { PasswordErrorMessage = AccountRegistrationService.ErrorMessageForPasswordBreakingTheRules };
        }

        // Le hachage est lent : il passe avant la lecture du compte, pour que l'écriture suive de près la lecture.
        var secureHashOfTheNewPassword = hasherOfThePasswordsChosenByVisitors.ConvertPasswordIntoItsSecureHash(passwordWrittenInItsNormalizedForm);

        return await SaveNewPasswordThenCloseEverySessionAsync(hashOfTheTokenFromTheLink, secureHashOfTheNewPassword, ipAddressOfTheRequestingVisitor);
    }

    // Enregistre le changement. Si une autre requête a changé le compte ou le lien, relit tout et recommence :
    // un lien déjà utilisé ou remplacé entre-temps est alors refusé.
    private async Task<PasswordResetResult> SaveNewPasswordThenCloseEverySessionAsync(string hashOfTheTokenFromTheLink, string secureHashOfTheNewPassword, IPAddress? ipAddressOfTheRequestingVisitor)
    {
        for (var numberOfTheTryBeingDone = 1; numberOfTheTryBeingDone <= AccountLockoutRules.MaximumNumberOfTriesWhileOtherRequestsChangeTheAccount; numberOfTheTryBeingDone++)
        {
            var tokenThatCanStillBeUsed = await FindRecoveryTokenThatCanStillBeUsedAsync(hashOfTheTokenFromTheLink);
            if (tokenThatCanStillBeUsed == null)
            {
                return CreateResultForTheLinkNoLongerValid();
            }

            var accountThatOwnsTheRecoveryToken = await repositoryOfTheUserAccountsInDatabase.FindUserAccountByIdentifierAsync(tokenThatCanStillBeUsed.UserId);
            if (accountThatOwnsTheRecoveryToken == null || !accountThatOwnsTheRecoveryToken.IsEmailConfirmed)
            {
                return CreateResultForTheLinkNoLongerValid();
            }

            AccountLockoutRules.RemoveCurrentLockoutOfThisAccount(accountThatOwnsTheRecoveryToken);

            var newPasswordWasSavedInTheDatabase = await repositoryOfTheUserAccountsInDatabase.TryToSaveNewPasswordAndUseItsTokenAsync(
                accountThatOwnsTheRecoveryToken,
                tokenThatCanStillBeUsed,
                secureHashOfTheNewPassword,
                clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime,
                CreateSecurityEventOfThePasswordChange(accountThatOwnsTheRecoveryToken.Id, ipAddressOfTheRequestingVisitor));

            if (newPasswordWasSavedInTheDatabase)
            {
                // Après l'enregistrement, et pas pendant : une connexion en cours avec l'ancien mot de passe a soit
                // déjà ouvert sa session, qui est effacée ici, soit trouvera le nouveau mot de passe et sera refusée (SS §3).
                await repositoryOfTheUserSessionsInDatabase.DeleteAllUserSessionsOfThisAccountAsync(accountThatOwnsTheRecoveryToken.Id);
                return new PasswordResetResult();
            }
        }

        return CreateResultForTheLinkNoLongerValid();
    }

    // Cherche le jeton du lien. Renvoie null s'il n'existe pas, s'il a déjà servi ou s'il a expiré.
    private async Task<EmailToken?> FindRecoveryTokenThatCanStillBeUsedAsync(string hashOfTheTokenFromTheLink)
    {
        var tokenFoundInTheDatabaseTable = await repositoryOfTheUserAccountsInDatabase.FindEmailTokenOfThisTypeByItsHashAsync(hashOfTheTokenFromTheLink, EmailTokenType.PasswordRecovery);

        if (tokenFoundInTheDatabaseTable == null
            || !EmailTokenRules.CheckThatTokenCanStillBeUsedAtThisDate(tokenFoundInTheDatabaseTable, clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime))
        {
            return null;
        }

        return tokenFoundInTheDatabaseTable;
    }

    // Crée l'événement du journal de sécurité (SS §11).
    private SecurityEvent CreateSecurityEventOfThePasswordChange(Guid identifierOfTheAccountWithNewPassword, IPAddress? ipAddressOfTheRequestingVisitor)
    {
        return new SecurityEvent
        {
            Type = SecurityEventType.PasswordChange,
            Result = SecurityEventResult.Success,
            OccurredAt = clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime,
            IpAddress = ipAddressOfTheRequestingVisitor,
            UserId = identifierOfTheAccountWithNewPassword
        };
    }

    // Donne le même refus pour tout lien qui ne vaut plus, sans dire pourquoi.
    private static PasswordResetResult CreateResultForTheLinkNoLongerValid()
    {
        return new PasswordResetResult { LinkErrorMessage = ErrorMessageForRecoveryLinkNoLongerValid };
    }
}
