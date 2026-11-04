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

    // Renvoie vrai si un compte utilise déjà cette adresse e-mail.
    public async Task<bool> CheckThatEmailAddressIsAlreadyUsedAsync(string emailAddressInItsStoredForm)
    {
        return await databaseContextOfTheCurrentHttpRequest.Users.AnyAsync(userAlreadySavedInTheDatabase =>
            userAlreadySavedInTheDatabase.Email == emailAddressInItsStoredForm);
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
