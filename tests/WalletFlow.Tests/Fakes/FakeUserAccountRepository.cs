using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde les comptes en mémoire.
public class FakeUserAccountRepository : IUserAccountRepository
{
    public List<User> SavedUsers { get; } = new List<User>();
    public List<Category> SavedCategories { get; } = new List<Category>();
    public List<EmailToken> SavedEmailTokens { get; } = new List<EmailToken>();

    // Simule une autre inscription qui prend la même adresse juste après la vérification.
    public bool AddressIsTakenJustBeforeTheSave { get; set; } = false;

    // Simule une panne de la base : chaque recherche d'adresse échoue avec ce détail technique.
    public bool DatabaseIsDownDuringThisTest { get; set; } = false;
    public const string TechnicalDetailOfTheSimulatedDatabaseFailure = "detail-technique-de-la-panne-simulee";

    // Renvoie vrai si un compte gardé en mémoire utilise déjà cette adresse.
    public Task<bool> CheckThatEmailAddressIsAlreadyUsedAsync(string emailAddressInItsStoredForm)
    {
        if (DatabaseIsDownDuringThisTest)
        {
            throw new InvalidOperationException(TechnicalDetailOfTheSimulatedDatabaseFailure);
        }

        foreach (var userAlreadyKeptInThisMemoryList in SavedUsers)
        {
            if (userAlreadyKeptInThisMemoryList.Email == emailAddressInItsStoredForm)
            {
                return Task.FromResult(true);
            }
        }

        return Task.FromResult(false);
    }

    // Garde le nouveau compte en mémoire, sauf si l'adresse vient d'être prise.
    public Task<bool> TryToSaveNewAccountWithCategoriesAndTokenAsync(
        User newUserToRegisterInTheDatabase,
        List<Category> defaultCategoriesOfTheNewUser,
        EmailToken signupConfirmationTokenOfTheNewUser)
    {
        if (AddressIsTakenJustBeforeTheSave)
        {
            return Task.FromResult(false);
        }

        SavedUsers.Add(newUserToRegisterInTheDatabase);
        SavedCategories.AddRange(defaultCategoriesOfTheNewUser);
        SavedEmailTokens.Add(signupConfirmationTokenOfTheNewUser);
        return Task.FromResult(true);
    }
}
