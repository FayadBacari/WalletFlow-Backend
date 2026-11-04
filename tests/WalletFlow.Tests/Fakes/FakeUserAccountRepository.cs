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

    // Cherche en mémoire le compte qui utilise cette adresse.
    public Task<User?> FindUserAccountByEmailAddressAsync(string emailAddressInItsStoredForm)
    {
        if (DatabaseIsDownDuringThisTest)
        {
            throw new InvalidOperationException(TechnicalDetailOfTheSimulatedDatabaseFailure);
        }

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
        foreach (var userAlreadyKeptInThisMemoryList in SavedUsers)
        {
            if (userAlreadyKeptInThisMemoryList.Id == identifierOfTheUserAccountToFind)
            {
                return Task.FromResult<User?>(userAlreadyKeptInThisMemoryList);
            }
        }

        return Task.FromResult<User?>(null);
    }

    // Cherche en mémoire un jeton de confirmation d'inscription par son empreinte.
    public Task<EmailToken?> FindSignupConfirmationTokenByItsHashAsync(string hashOfTheTokenFromTheLink)
    {
        foreach (var tokenAlreadyKeptInThisMemoryList in SavedEmailTokens)
        {
            if (tokenAlreadyKeptInThisMemoryList.TokenHash == hashOfTheTokenFromTheLink
                && tokenAlreadyKeptInThisMemoryList.Type == EmailTokenType.SignupConfirmation)
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
