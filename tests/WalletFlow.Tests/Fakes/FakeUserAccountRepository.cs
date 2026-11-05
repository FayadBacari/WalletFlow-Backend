using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde les comptes en mémoire.
public class FakeUserAccountRepository : IUserAccountRepository
{
    public const string TechnicalDetailOfTheSimulatedDatabaseFailure = "detail-technique-de-la-panne-simulee";

    public List<User> SavedUsers { get; } = new List<User>();
    public List<Category> SavedCategories { get; } = new List<Category>();
    public List<EmailToken> SavedEmailTokens { get; } = new List<EmailToken>();
    public List<DateTime> DatesOfTheCleanupsRequested { get; } = new List<DateTime>();

    // Simule une autre inscription qui prend la même adresse juste après la recherche.
    public User? AccountSavedByAnotherRequestJustBeforeTheSave { get; set; } = null;

    // Simule une confirmation qui arrive juste avant le remplacement du mot de passe.
    public bool AccountIsConfirmedJustBeforeThePasswordChange { get; set; } = false;

    // Simule une nouvelle inscription qui remplace le lien juste avant la confirmation.
    public bool TokenIsReplacedJustBeforeTheConfirmation { get; set; } = false;

    // Simule une panne de la base : chaque recherche d'adresse échoue avec ce détail technique.
    public bool DatabaseIsDownDuringThisTest { get; set; } = false;

    // Simule une panne de la base pendant l'effacement des comptes non confirmés.
    public bool DatabaseIsDownDuringTheCleanup { get; set; } = false;

    // Simule d'autres requêtes qui comptent leur essai juste avant l'enregistrement : chacune provoque un conflit.
    public int NumberOfConflictsBeforeTheLoginCountersAreSaved { get; set; } = 0;

    // Simule un compte effacé pendant la connexion : au-delà de ce nombre de recherches par identifiant, il n'existe plus.
    public int? NumberOfSearchesByIdentifierBeforeTheAccountDisappears { get; set; } = null;

    // Simule d'autres requêtes qui changent le compte juste avant l'enregistrement d'un nouveau mot de passe ou d'un déblocage.
    public int NumberOfConflictsBeforeTheLinkIsUsed { get; set; } = 0;

    // Simule une autre requête qui utilise le même lien juste avant l'enregistrement.
    public bool LinkIsUsedByAnotherRequestJustBeforeTheSave { get; set; } = false;

    // Simule une panne de la base au moment de lire un lien.
    public bool DatabaseIsDownWhenReadingTheLinks { get; set; } = false;

    public List<SecurityEvent> SecurityEventsSavedWithTheLinks { get; } = new List<SecurityEvent>();

    public int NumberOfSearchesByEmailAddress { get; private set; } = 0;
    public int NumberOfLoginCountersSaved { get; private set; } = 0;

    private short failedAttemptsReadBeforeTheSaveOfTheCounters = 0;
    private short lockoutCountReadBeforeTheSaveOfTheCounters = 0;
    private DateTime? lockoutEndReadBeforeTheSaveOfTheCounters = null;

    // Cherche en mémoire le compte qui utilise cette adresse.
    public Task<User?> FindUserAccountByEmailAddressAsync(string emailAddressInItsStoredForm)
    {
        if (DatabaseIsDownDuringThisTest)
        {
            throw new InvalidOperationException(TechnicalDetailOfTheSimulatedDatabaseFailure);
        }

        NumberOfSearchesByEmailAddress = NumberOfSearchesByEmailAddress + 1;

        foreach (var userAlreadyKeptInThisMemoryList in SavedUsers)
        {
            if (userAlreadyKeptInThisMemoryList.Email == emailAddressInItsStoredForm)
            {
                return Task.FromResult<User?>(userAlreadyKeptInThisMemoryList);
            }
        }

        return Task.FromResult<User?>(null);
    }

    // Cherche en mémoire un compte par son identifiant.
    public Task<User?> FindUserAccountByIdentifierAsync(Guid identifierOfTheUserAccountToFind)
    {
        if (NumberOfSearchesByIdentifierBeforeTheAccountDisappears == 0)
        {
            return Task.FromResult<User?>(null);
        }

        NumberOfSearchesByIdentifierBeforeTheAccountDisappears = NumberOfSearchesByIdentifierBeforeTheAccountDisappears - 1;

        foreach (var userAlreadyKeptInThisMemoryList in SavedUsers)
        {
            if (userAlreadyKeptInThisMemoryList.Id == identifierOfTheUserAccountToFind)
            {
                failedAttemptsReadBeforeTheSaveOfTheCounters = userAlreadyKeptInThisMemoryList.FailedLoginAttemptCount;
                lockoutCountReadBeforeTheSaveOfTheCounters = userAlreadyKeptInThisMemoryList.LockoutCount;
                lockoutEndReadBeforeTheSaveOfTheCounters = userAlreadyKeptInThisMemoryList.LockoutEndsAt;

                return Task.FromResult<User?>(userAlreadyKeptInThisMemoryList);
            }
        }

        return Task.FromResult<User?>(null);
    }

    // Comme la vraie base, un conflit annule ce que la requête voulait écrire : le compte repart des valeurs lues,
    // plus l'essai compté par l'autre requête.
    public Task<bool> TryToSaveLoginCountersOfThisAccountAsync(User accountWithItsNewLoginCounters)
    {
        if (NumberOfConflictsBeforeTheLoginCountersAreSaved > 0)
        {
            NumberOfConflictsBeforeTheLoginCountersAreSaved = NumberOfConflictsBeforeTheLoginCountersAreSaved - 1;
            accountWithItsNewLoginCounters.FailedLoginAttemptCount = (short)(failedAttemptsReadBeforeTheSaveOfTheCounters + 1);
            accountWithItsNewLoginCounters.LockoutCount = lockoutCountReadBeforeTheSaveOfTheCounters;
            accountWithItsNewLoginCounters.LockoutEndsAt = lockoutEndReadBeforeTheSaveOfTheCounters;
            return Task.FromResult(false);
        }

        NumberOfLoginCountersSaved = NumberOfLoginCountersSaved + 1;
        return Task.FromResult(true);
    }

    // Garde le nouveau jeton en mémoire, et retire l'ancien du même type et du même compte.
    public Task ReplaceEmailTokenOfThisTypeForThisAccountAsync(EmailToken newEmailTokenOfTheAccount)
    {
        SavedEmailTokens.RemoveAll(tokenAlreadyKeptInThisMemoryList =>
            tokenAlreadyKeptInThisMemoryList.UserId == newEmailTokenOfTheAccount.UserId
            && tokenAlreadyKeptInThisMemoryList.Type == newEmailTokenOfTheAccount.Type);
        SavedEmailTokens.Add(newEmailTokenOfTheAccount);

        return Task.CompletedTask;
    }

    // Garde le nouveau mot de passe et marque le jeton comme utilisé, sauf si un conflit est simulé.
    public Task<bool> TryToSaveNewPasswordAndUseItsTokenAsync(
        User accountThatReceivesTheNewPassword,
        EmailToken tokenUsedToChangeThePassword,
        string secureHashOfTheNewPassword,
        DateTime dateOfThePasswordChangeOfTheAccount,
        SecurityEvent securityEventOfThePasswordChange)
    {
        if (CheckThatConflictIsSimulatedForThisLink(tokenUsedToChangeThePassword, dateOfThePasswordChangeOfTheAccount))
        {
            return Task.FromResult(false);
        }

        accountThatReceivesTheNewPassword.PasswordHash = secureHashOfTheNewPassword;
        tokenUsedToChangeThePassword.UsedAt = dateOfThePasswordChangeOfTheAccount;
        SecurityEventsSavedWithTheLinks.Add(securityEventOfThePasswordChange);

        return Task.FromResult(true);
    }

    // Marque le jeton comme utilisé, sauf si un conflit est simulé.
    public Task<bool> TryToUnlockAccountAndUseItsTokenAsync(
        User accountJustUnlockedByTheLink,
        EmailToken tokenUsedToUnlockTheAccount,
        DateTime dateOfTheAccountUnlockByTheLink,
        SecurityEvent securityEventOfTheAccountUnlock)
    {
        if (CheckThatConflictIsSimulatedForThisLink(tokenUsedToUnlockTheAccount, dateOfTheAccountUnlockByTheLink))
        {
            return Task.FromResult(false);
        }

        tokenUsedToUnlockTheAccount.UsedAt = dateOfTheAccountUnlockByTheLink;
        SecurityEventsSavedWithTheLinks.Add(securityEventOfTheAccountUnlock);

        return Task.FromResult(true);
    }

    // Renvoie vrai si un conflit est simulé pour cet enregistrement.
    private bool CheckThatConflictIsSimulatedForThisLink(EmailToken tokenOfTheLinkBeingUsedNow, DateTime dateOfTheCurrentUseOfTheLink)
    {
        if (LinkIsUsedByAnotherRequestJustBeforeTheSave)
        {
            tokenOfTheLinkBeingUsedNow.UsedAt = dateOfTheCurrentUseOfTheLink;
            return true;
        }

        if (NumberOfConflictsBeforeTheLinkIsUsed > 0)
        {
            NumberOfConflictsBeforeTheLinkIsUsed = NumberOfConflictsBeforeTheLinkIsUsed - 1;
            return true;
        }

        return false;
    }

    // Cherche en mémoire un jeton de ce type par son empreinte.
    public Task<EmailToken?> FindEmailTokenOfThisTypeByItsHashAsync(string hashOfTheTokenFromTheLink, EmailTokenType typeOfTheTokenToFindNow)
    {
        if (DatabaseIsDownWhenReadingTheLinks)
        {
            throw new InvalidOperationException(TechnicalDetailOfTheSimulatedDatabaseFailure);
        }

        foreach (var tokenAlreadyKeptInThisMemoryList in SavedEmailTokens)
        {
            if (tokenAlreadyKeptInThisMemoryList.TokenHash == hashOfTheTokenFromTheLink
                && tokenAlreadyKeptInThisMemoryList.Type == typeOfTheTokenToFindNow)
            {
                return Task.FromResult<EmailToken?>(tokenAlreadyKeptInThisMemoryList);
            }
        }

        return Task.FromResult<EmailToken?>(null);
    }

    // Garde le nouveau compte en mémoire, sauf si l'adresse vient d'être prise.
    public Task<bool> TryToSaveNewAccountWithCategoriesAndTokenAsync(
        User newUserToRegisterInTheDatabase,
        List<Category> defaultCategoriesOfTheNewUser,
        EmailToken signupConfirmationTokenOfTheNewUser)
    {
        if (AccountSavedByAnotherRequestJustBeforeTheSave != null)
        {
            SavedUsers.Add(AccountSavedByAnotherRequestJustBeforeTheSave);
            return Task.FromResult(false);
        }

        SavedUsers.Add(newUserToRegisterInTheDatabase);
        SavedCategories.AddRange(defaultCategoriesOfTheNewUser);
        SavedEmailTokens.Add(signupConfirmationTokenOfTheNewUser);
        return Task.FromResult(true);
    }

    // Remplace le mot de passe et le lien, sauf si le compte vient d'être confirmé.
    public Task<bool> TryToReplacePasswordAndTokenOfUnconfirmedAccountAsync(
        User unconfirmedAccountToUpdateInTheDatabase,
        string newSecureHashOfThePassword,
        EmailToken newSignupConfirmationTokenOfTheAccount)
    {
        if (AccountIsConfirmedJustBeforeThePasswordChange)
        {
            unconfirmedAccountToUpdateInTheDatabase.IsEmailConfirmed = true;
            return Task.FromResult(false);
        }

        unconfirmedAccountToUpdateInTheDatabase.PasswordHash = newSecureHashOfThePassword;
        SavedEmailTokens.RemoveAll(tokenAlreadyKeptInThisMemoryList => tokenAlreadyKeptInThisMemoryList.UserId == unconfirmedAccountToUpdateInTheDatabase.Id);
        SavedEmailTokens.Add(newSignupConfirmationTokenOfTheAccount);
        return Task.FromResult(true);
    }

    // Confirme le compte et marque son jeton comme utilisé, sauf si le lien vient d'être remplacé.
    public Task<bool> TryToConfirmAccountAndUseItsTokenAsync(
        User accountToConfirmInTheDatabase,
        EmailToken tokenUsedToConfirmTheAccount,
        DateTime dateOfTheConfirmationOfTheAccount)
    {
        if (TokenIsReplacedJustBeforeTheConfirmation)
        {
            return Task.FromResult(false);
        }

        accountToConfirmInTheDatabase.IsEmailConfirmed = true;
        tokenUsedToConfirmTheAccount.UsedAt = dateOfTheConfirmationOfTheAccount;
        return Task.FromResult(true);
    }

    // Garde la date du passage. La vraie règle d'effacement est testée sur la vraie base.
    public Task<int> DeleteUnconfirmedAccountsWithExpiredLinkAsync(DateTime currentDateUsedForTheCleanup)
    {
        if (DatabaseIsDownDuringTheCleanup)
        {
            throw new InvalidOperationException(TechnicalDetailOfTheSimulatedDatabaseFailure);
        }

        DatesOfTheCleanupsRequested.Add(currentDateUsedForTheCleanup);
        return Task.FromResult(0);
    }
}
