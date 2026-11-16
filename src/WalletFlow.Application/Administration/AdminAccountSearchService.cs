using System.Net;
using WalletFlow.Application.Security;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Administration;

// Cas d'utilisation : l'administrateur retrouve un compte par son adresse exacte, avec son état (GR Admin). Il ne peut pas
// parcourir la liste des utilisateurs. Chaque recherche est notée au journal, avec le compte trouvé : elle montre des données
// personnelles (SS §8, §11).
public class AdminAccountSearchService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly SecurityJournalService securityJournal;
    private readonly TimeProvider clock;

    // Reçoit l'accès aux comptes, le journal de sécurité et l'horloge qui dit si le compte est bloqué.
    public AdminAccountSearchService(IUserAccountRepository accountRepository, SecurityJournalService securityJournal, TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.securityJournal = securityJournal;
        this.clock = clock;
    }

    // Cherche le compte qui utilise exactement cette adresse, une fois mise en minuscules et sans espaces autour.
    // Une adresse mal formée n'est pas cherchée, et ne laisse rien au journal.
    public async Task<AdminAccountSearchResult> SearchAsync(Guid administratorId, string? typedEmail, IPAddress? ipAddress)
    {
        var email = EmailAddressRules.Normalize(typedEmail ?? string.Empty);

        if (!EmailAddressRules.IsValid(email))
        {
            return new AdminAccountSearchResult.EmailBreaksRules();
        }

        var account = await accountRepository.FindByEmailAsync(email);

        await securityJournal.SaveAdminActionNowAsync(administratorId, account?.Id, ipAddress);

        if (account == null)
        {
            return new AdminAccountSearchResult.NotFound();
        }

        return new AdminAccountSearchResult.Found { Account = AdminAccountView.Create(account, clock.GetUtcNow().UtcDateTime) };
    }
}
