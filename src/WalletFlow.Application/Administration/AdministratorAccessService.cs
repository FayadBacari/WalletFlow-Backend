using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Administration;

// Cas d'utilisation : vérifie, à chaque requête d'administration, que l'utilisateur de la session est administrateur et qu'il a
// activé la double authentification (GR Admin, SS §4 et §8). Le rôle est relu en base à chaque fois : un rôle retiré par
// fn_changer_role ne sert plus dès la requête suivante, même dans une session déjà ouverte.
public class AdministratorAccessService
{
    private readonly IUserAccountRepository accountRepository;

    // Reçoit l'accès aux comptes.
    public AdministratorAccessService(IUserAccountRepository accountRepository)
    {
        this.accountRepository = accountRepository;
    }

    // Dit si l'utilisateur peut utiliser l'administration. Un compte qui n'existe plus n'est pas administrateur.
    public async Task<AdministratorAccess> CheckAsync(Guid userId)
    {
        var account = await accountRepository.FindByIdAsync(userId);

        if (account == null || account.Role != UserRole.Administrator)
        {
            return AdministratorAccess.NotAdministrator;
        }

        if (!account.IsTwoFactorEnabled)
        {
            return AdministratorAccess.TwoFactorRequired;
        }

        return AdministratorAccess.Granted;
    }
}
