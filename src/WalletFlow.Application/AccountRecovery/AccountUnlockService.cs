using System.Net;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountRecovery;

// Cas d'utilisation : un utilisateur débloque son compte avec le lien reçu par e-mail (GR Auth, SS §2).
// Le lien remet les essais à zéro et termine le blocage, mais il ne connecte personne.
public class AccountUnlockService
{
    public const string ErrorMessageForUnlockLinkNoLongerValid = "Ce lien n'est plus valable. Si ton compte est encore bloqué, utilise « Mot de passe oublié ».";

    private readonly IUserAccountRepository repositoryOfTheUserAccountsInDatabase;
    private readonly ISecureTokenGenerator generatorOfTheSecureRandomTokens;
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;

    // Reçoit les outils dont le déblocage a besoin.
    public AccountUnlockService(
        IUserAccountRepository repositoryOfTheUserAccountsInDatabase,
        ISecureTokenGenerator generatorOfTheSecureRandomTokens,
        TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.repositoryOfTheUserAccountsInDatabase = repositoryOfTheUserAccountsInDatabase;
        this.generatorOfTheSecureRandomTokens = generatorOfTheSecureRandomTokens;
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Débloque le compte si le lien est encore valable. Si une autre requête a changé le compte ou le lien,
    // relit tout et recommence : un lien déjà utilisé ou remplacé entre-temps est alors refusé.
    public async Task<AccountUnlockResult> UnlockAccountWithLinkTokenAsync(string tokenReadInTheUnlockLink, IPAddress? ipAddressOfTheRequestingVisitor)
    {
        var hashOfTheTokenFromTheLink = generatorOfTheSecureRandomTokens.ConvertSecureTokenIntoItsHash(tokenReadInTheUnlockLink);

        for (var numberOfTheTryBeingDone = 1; numberOfTheTryBeingDone <= AccountLockoutRules.MaximumNumberOfTriesWhileOtherRequestsChangeTheAccount; numberOfTheTryBeingDone++)
        {
            var currentDateOfTheAccountUnlock = clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime;
            var tokenFoundInTheDatabaseTable = await repositoryOfTheUserAccountsInDatabase.FindEmailTokenOfThisTypeByItsHashAsync(hashOfTheTokenFromTheLink, EmailTokenType.AccountUnlock);

            if (tokenFoundInTheDatabaseTable == null || !EmailTokenRules.CheckThatTokenCanStillBeUsedAtThisDate(tokenFoundInTheDatabaseTable, currentDateOfTheAccountUnlock))
            {
                return CreateResultForTheLinkNoLongerValid();
            }

            var accountThatOwnsTheUnlockToken = await repositoryOfTheUserAccountsInDatabase.FindUserAccountByIdentifierAsync(tokenFoundInTheDatabaseTable.UserId);
            if (accountThatOwnsTheUnlockToken == null)
            {
                return CreateResultForTheLinkNoLongerValid();
            }

            AccountLockoutRules.RemoveCurrentLockoutOfThisAccount(accountThatOwnsTheUnlockToken);

            var accountWasUnlockedInTheDatabase = await repositoryOfTheUserAccountsInDatabase.TryToUnlockAccountAndUseItsTokenAsync(
                accountThatOwnsTheUnlockToken,
                tokenFoundInTheDatabaseTable,
                currentDateOfTheAccountUnlock,
                CreateSecurityEventOfTheAccountUnlock(accountThatOwnsTheUnlockToken.Id, currentDateOfTheAccountUnlock, ipAddressOfTheRequestingVisitor));

            if (accountWasUnlockedInTheDatabase)
            {
                return new AccountUnlockResult();
            }
        }

        return CreateResultForTheLinkNoLongerValid();
    }

    // Crée l'événement du journal de sécurité (SS §11).
    private static SecurityEvent CreateSecurityEventOfTheAccountUnlock(Guid identifierOfTheAccountJustUnlocked, DateTime currentDateOfTheAccountUnlock, IPAddress? ipAddressOfTheRequestingVisitor)
    {
        return new SecurityEvent
        {
            Type = SecurityEventType.Unlock,
            Result = SecurityEventResult.Success,
            OccurredAt = currentDateOfTheAccountUnlock,
            IpAddress = ipAddressOfTheRequestingVisitor,
            UserId = identifierOfTheAccountJustUnlocked
        };
    }

    // Donne le même refus pour tout lien qui ne vaut plus, sans dire pourquoi.
    private static AccountUnlockResult CreateResultForTheLinkNoLongerValid()
    {
        return new AccountUnlockResult { LinkErrorMessage = ErrorMessageForUnlockLinkNoLongerValid };
    }
}
