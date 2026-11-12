using WalletFlow.Application.UserAccounts;

namespace WalletFlow.Application.AccountSettings;

// Cas d'utilisation : l'utilisateur connecté lit les informations de son compte (GR Compte).
public class ProfileService
{
    private readonly IUserAccountRepository accountRepository;

    // Reçoit l'accès aux comptes.
    public ProfileService(IUserAccountRepository accountRepository)
    {
        this.accountRepository = accountRepository;
    }

    // Donne le profil du compte, ou null s'il n'existe plus. Ni le mot de passe ni aucun secret n'en font partie.
    public async Task<AccountProfile?> FindAsync(Guid userId)
    {
        var account = await accountRepository.FindByIdAsync(userId);

        if (account == null)
        {
            return null;
        }

        return new AccountProfile
        {
            Email = account.Email,
            LastName = account.LastName,
            FirstName = account.FirstName,
            IsTwoFactorEnabled = account.IsTwoFactorEnabled
        };
    }
}
