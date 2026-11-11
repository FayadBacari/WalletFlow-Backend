namespace WalletFlow.Application.UserAccounts;

// Cas d'utilisation : efface les comptes non confirmés une fois leur lien expiré (GR Auth).
// On ne garde pas l'adresse d'une personne qui ne s'est peut-être jamais inscrite.
public class UnconfirmedAccountsCleanupService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly TimeProvider clock;

    // Reçoit l'accès à la base et l'horloge.
    public UnconfirmedAccountsCleanupService(
        IUserAccountRepository accountRepository,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.clock = clock;
    }

    // Efface les comptes concernés, et renvoie leur nombre.
    public async Task<int> DeleteExpiredUnconfirmedAccountsAsync()
    {
        var now = clock.GetUtcNow().UtcDateTime;

        return await accountRepository.DeleteExpiredUnconfirmedAccountsAsync(now);
    }
}
