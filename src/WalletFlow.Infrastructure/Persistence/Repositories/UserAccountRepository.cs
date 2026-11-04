using Microsoft.EntityFrameworkCore;
using Npgsql;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Lit et écrit les comptes des utilisateurs dans la base PostgreSQL.
public class UserAccountRepository : IUserAccountRepository
{
    // Nom donné par PostgreSQL à la contrainte UNIQUE de la colonne email_utilisateur.
    private const string NameOfTheEmailAddressUniqueConstraint = "utilisateur_email_utilisateur_key";

    private readonly WalletFlowDatabaseContext databaseContextOfTheCurrentHttpRequest;

    // Reçoit l'accès à la base de la requête en cours.
    public UserAccountRepository(WalletFlowDatabaseContext databaseContextOfTheCurrentHttpRequest)
    {
        this.databaseContextOfTheCurrentHttpRequest = databaseContextOfTheCurrentHttpRequest;
    }

    // Cherche le compte qui utilise cette adresse e-mail. Renvoie null s'il n'y en a pas.
    public async Task<User?> FindUserAccountByEmailAddressAsync(string emailAddressInItsStoredForm)
    {
        return await databaseContextOfTheCurrentHttpRequest.Users.SingleOrDefaultAsync(userAlreadySavedInTheDatabase =>
            userAlreadySavedInTheDatabase.Email == emailAddressInItsStoredForm);
    }

    // Cherche un compte par son identifiant. Renvoie null s'il n'existe plus.
    public async Task<User?> FindUserAccountByIdentifierAsync(Guid identifierOfTheUserAccountToFind)
    {
        return await databaseContextOfTheCurrentHttpRequest.Users.SingleOrDefaultAsync(userAlreadySavedInTheDatabase =>
            userAlreadySavedInTheDatabase.Id == identifierOfTheUserAccountToFind);
    }

    // Cherche un jeton de confirmation d'inscription par son empreinte. Renvoie null s'il n'existe pas.
    public async Task<EmailToken?> FindSignupConfirmationTokenByItsHashAsync(string hashOfTheTokenFromTheLink)
    {
        return await databaseContextOfTheCurrentHttpRequest.EmailTokens.SingleOrDefaultAsync(tokenAlreadySavedInTheDatabase =>
            tokenAlreadySavedInTheDatabase.TokenHash == hashOfTheTokenFromTheLink
            && tokenAlreadySavedInTheDatabase.Type == EmailTokenType.SignupConfirmation);
    }

    // Enregistre le nouvel utilisateur, ses catégories et son jeton en une seule transaction SQL :
    // SaveChangesAsync enregistre tout, ou rien (MPD §3).
    // Renvoie faux si une autre inscription vient de prendre la même adresse.
    public async Task<bool> TryToSaveNewAccountWithCategoriesAndTokenAsync(
        User newUserToRegisterInTheDatabase,
        List<Category> defaultCategoriesOfTheNewUser,
        EmailToken signupConfirmationTokenOfTheNewUser)
    {
        databaseContextOfTheCurrentHttpRequest.Users.Add(newUserToRegisterInTheDatabase);
        databaseContextOfTheCurrentHttpRequest.Categories.AddRange(defaultCategoriesOfTheNewUser);
        databaseContextOfTheCurrentHttpRequest.EmailTokens.Add(signupConfirmationTokenOfTheNewUser);

        try
        {
            await databaseContextOfTheCurrentHttpRequest.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException exceptionRaisedWhileSavingTheNewAccount)
        {
            if (!CheckThatExceptionComesFromTheEmailAddressConstraint(exceptionRaisedWhileSavingTheNewAccount))
            {
                throw;
            }

            databaseContextOfTheCurrentHttpRequest.ChangeTracker.Clear();
            return false;
        }
    }

    // Remplace le mot de passe et le lien d'un compte non confirmé, en une seule transaction SQL.
    // Renvoie faux si le compte vient d'être confirmé ou effacé : le contrôle sur email_confirme
    // empêche alors de remplacer le mot de passe d'un compte déjà actif.
    public async Task<bool> TryToReplacePasswordAndTokenOfUnconfirmedAccountAsync(
        User unconfirmedAccountToUpdateInTheDatabase,
        string newSecureHashOfThePassword,
        EmailToken newSignupConfirmationTokenOfTheAccount)
    {
        var oldTokensOfTheUnconfirmedAccount = await databaseContextOfTheCurrentHttpRequest.EmailTokens
            .Where(tokenAlreadySavedInTheDatabase =>
                tokenAlreadySavedInTheDatabase.UserId == unconfirmedAccountToUpdateInTheDatabase.Id
                && tokenAlreadySavedInTheDatabase.Type == EmailTokenType.SignupConfirmation)
            .ToListAsync();

        unconfirmedAccountToUpdateInTheDatabase.PasswordHash = newSecureHashOfThePassword;
        databaseContextOfTheCurrentHttpRequest.EmailTokens.RemoveRange(oldTokensOfTheUnconfirmedAccount);
        databaseContextOfTheCurrentHttpRequest.EmailTokens.Add(newSignupConfirmationTokenOfTheAccount);

        return await databaseContextOfTheCurrentHttpRequest.TryToSaveChangesUnlessAnotherRequestChangedTheSameRowsAsync();
    }

    // Confirme le compte et marque son jeton comme utilisé, en une seule transaction SQL.
    // Renvoie faux si le compte ou le jeton vient de changer : rien n'est alors modifié.
    public async Task<bool> TryToConfirmAccountAndUseItsTokenAsync(
        User accountToConfirmInTheDatabase,
        EmailToken tokenUsedToConfirmTheAccount,
        DateTime dateOfTheConfirmationOfTheAccount)
    {
        accountToConfirmInTheDatabase.IsEmailConfirmed = true;
        tokenUsedToConfirmTheAccount.UsedAt = dateOfTheConfirmationOfTheAccount;

        return await databaseContextOfTheCurrentHttpRequest.TryToSaveChangesUnlessAnotherRequestChangedTheSameRowsAsync();
    }

    // Enregistre les compteurs de connexion du compte : EF Core suit ce compte depuis sa lecture, et n'écrit que ce qui a changé.
    // Le contrôle sur nb_tentatives_echouees et date_fin_blocage renvoie faux si une autre requête vient de les changer :
    // aucun essai ne peut donc se perdre, et aucun blocage ne peut être effacé.
    public async Task<bool> TryToSaveLoginCountersOfThisAccountAsync(User accountWithItsNewLoginCounters)
    {
        return await databaseContextOfTheCurrentHttpRequest.TryToSaveChangesUnlessAnotherRequestChangedTheSameRowsAsync();
    }

    // Efface les comptes non confirmés qui n'ont plus aucun lien valable, et renvoie leur nombre.
    // Leurs catégories et leurs jetons partent avec eux (ON DELETE CASCADE).
    public async Task<int> DeleteUnconfirmedAccountsWithExpiredLinkAsync(DateTime currentDateUsedForTheCleanup)
    {
        return await databaseContextOfTheCurrentHttpRequest.Users
            .Where(userAlreadySavedInTheDatabase =>
                !userAlreadySavedInTheDatabase.IsEmailConfirmed
                && !databaseContextOfTheCurrentHttpRequest.EmailTokens.Any(tokenAlreadySavedInTheDatabase =>
                    tokenAlreadySavedInTheDatabase.UserId == userAlreadySavedInTheDatabase.Id
                    && tokenAlreadySavedInTheDatabase.Type == EmailTokenType.SignupConfirmation
                    && tokenAlreadySavedInTheDatabase.ExpiresAt > currentDateUsedForTheCleanup))
            .ExecuteDeleteAsync();
    }

    // Renvoie vrai si l'erreur vient de la contrainte d'unicité de l'adresse e-mail.
    private static bool CheckThatExceptionComesFromTheEmailAddressConstraint(DbUpdateException exceptionRaisedWhileSavingTheNewAccount)
    {
        if (exceptionRaisedWhileSavingTheNewAccount.InnerException is not PostgresException errorReturnedByTheDatabaseServer)
        {
            return false;
        }

        return errorReturnedByTheDatabaseServer.ConstraintName == NameOfTheEmailAddressUniqueConstraint;
    }
}
