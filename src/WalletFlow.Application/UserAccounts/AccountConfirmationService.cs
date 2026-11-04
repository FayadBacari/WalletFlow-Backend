using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserAccounts;

// Cas d'utilisation : un visiteur confirme son adresse avec le lien reçu et son mot de passe (GR Auth, SS §2).
// Il faut avoir à la fois la boîte mail et le mot de passe : ouvrir le lien seul n'active rien.
public class AccountConfirmationService
{
    public const string ErrorMessageForLinkNoLongerValid = "Ce lien n'est plus valable. Inscris-toi à nouveau pour en recevoir un autre.";

    public const string ErrorMessageForWrongPasswordOnConfirmation = "Ce n'est pas le mot de passe choisi à l'inscription. Si tu l'as oublié, inscris-toi à nouveau.";

    private readonly IUserAccountRepository repositoryOfTheUserAccountsInDatabase;
    private readonly IPasswordHasher hasherOfThePasswordsChosenByVisitors;
    private readonly ISecureTokenGenerator generatorOfTheSecureRandomEmailTokens;
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;

    // Reçoit les outils dont la confirmation a besoin.
    public AccountConfirmationService(
        IUserAccountRepository repositoryOfTheUserAccountsInDatabase,
        IPasswordHasher hasherOfThePasswordsChosenByVisitors,
        ISecureTokenGenerator generatorOfTheSecureRandomEmailTokens,
        TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.repositoryOfTheUserAccountsInDatabase = repositoryOfTheUserAccountsInDatabase;
        this.hasherOfThePasswordsChosenByVisitors = hasherOfThePasswordsChosenByVisitors;
        this.generatorOfTheSecureRandomEmailTokens = generatorOfTheSecureRandomEmailTokens;
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Confirme le compte si le lien est encore valable et si le mot de passe est le bon.
    // Un mauvais mot de passe ne consomme pas le lien : le visiteur peut réessayer.
    public async Task<AccountConfirmationResult> ConfirmAccountWithLinkTokenAndPasswordAsync(
        string tokenReadInTheConfirmationLink,
        string passwordTypedOnTheConfirmationPage)
    {
        var currentDateOfTheAccountConfirmation = clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime;

        var tokenThatCanStillBeUsed = await FindTokenThatCanStillBeUsedAtThisDateAsync(tokenReadInTheConfirmationLink, currentDateOfTheAccountConfirmation);
        if (tokenThatCanStillBeUsed == null)
        {
            return CreateResultForTheLinkNoLongerValid();
        }

        var accountThatOwnsTheConfirmationToken = await repositoryOfTheUserAccountsInDatabase.FindUserAccountByIdentifierAsync(tokenThatCanStillBeUsed.UserId);
        if (accountThatOwnsTheConfirmationToken == null || accountThatOwnsTheConfirmationToken.IsEmailConfirmed)
        {
            return CreateResultForTheLinkNoLongerValid();
        }

        if (!hasherOfThePasswordsChosenByVisitors.CheckThatPasswordMatchesItsSecureHash(passwordTypedOnTheConfirmationPage, accountThatOwnsTheConfirmationToken.PasswordHash))
        {
            return new AccountConfirmationResult { PasswordErrorMessage = ErrorMessageForWrongPasswordOnConfirmation };
        }

        // Une nouvelle inscription a pu remplacer ce lien pendant la vérification du mot de passe.
        var accountWasConfirmedInTheDatabase = await repositoryOfTheUserAccountsInDatabase.TryToConfirmAccountAndUseItsTokenAsync(
            accountThatOwnsTheConfirmationToken,
            tokenThatCanStillBeUsed,
            currentDateOfTheAccountConfirmation);

        if (!accountWasConfirmedInTheDatabase)
        {
            return CreateResultForTheLinkNoLongerValid();
        }

        return new AccountConfirmationResult();
    }

    // Cherche le jeton du lien. Renvoie null s'il n'existe pas, s'il a déjà servi ou s'il a expiré.
    private async Task<EmailToken?> FindTokenThatCanStillBeUsedAtThisDateAsync(string tokenReadInTheConfirmationLink, DateTime currentDateOfTheAccountConfirmation)
    {
        var hashOfTheTokenFromTheLink = generatorOfTheSecureRandomEmailTokens.ConvertSecureTokenIntoItsHash(tokenReadInTheConfirmationLink);
        var tokenFoundInTheDatabaseTable = await repositoryOfTheUserAccountsInDatabase.FindSignupConfirmationTokenByItsHashAsync(hashOfTheTokenFromTheLink);

        if (tokenFoundInTheDatabaseTable == null || tokenFoundInTheDatabaseTable.UsedAt != null || tokenFoundInTheDatabaseTable.ExpiresAt <= currentDateOfTheAccountConfirmation)
        {
            return null;
        }

        return tokenFoundInTheDatabaseTable;
    }

    // Donne le même refus pour tout lien qui ne vaut plus, sans dire pourquoi.
    private static AccountConfirmationResult CreateResultForTheLinkNoLongerValid()
    {
        return new AccountConfirmationResult { LinkErrorMessage = ErrorMessageForLinkNoLongerValid };
    }
}
