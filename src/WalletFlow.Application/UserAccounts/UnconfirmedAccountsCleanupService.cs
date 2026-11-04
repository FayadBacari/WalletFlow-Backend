namespace WalletFlow.Application.UserAccounts;

// Cas d'utilisation : efface les comptes non confirmés une fois leur lien expiré (GR Auth).
// On ne garde pas l'adresse d'une personne qui ne s'est peut-être jamais inscrite.
public class UnconfirmedAccountsCleanupService
{
    private readonly IUserAccountRepository repositoryOfTheUserAccountsInDatabase;
    private readonly TimeProvider clockThatGivesTheCurrentDateAndTime;

    // Reçoit l'accès à la base et l'horloge.
    public UnconfirmedAccountsCleanupService(
        IUserAccountRepository repositoryOfTheUserAccountsInDatabase,
        TimeProvider clockThatGivesTheCurrentDateAndTime)
    {
        this.repositoryOfTheUserAccountsInDatabase = repositoryOfTheUserAccountsInDatabase;
        this.clockThatGivesTheCurrentDateAndTime = clockThatGivesTheCurrentDateAndTime;
    }

    // Efface les comptes concernés, et renvoie leur nombre.
    public async Task<int> DeleteUnconfirmedAccountsWithExpiredLinkAsync()
    {
        var currentDateUsedForTheCleanup = clockThatGivesTheCurrentDateAndTime.GetUtcNow().UtcDateTime;

        return await repositoryOfTheUserAccountsInDatabase.DeleteUnconfirmedAccountsWithExpiredLinkAsync(currentDateUsedForTheCleanup);
    }
}
