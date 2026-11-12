using WalletFlow.Application.Persistence;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.AccountSettings;

// Cas d'utilisation : l'utilisateur connecté change ou efface son nom et son prénom (GR Compte).
// Ce ne sont pas des actions sensibles : aucun mot de passe n'est demandé.
public class PersonalNameChangeService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly DatabaseChangesSavingService savingService;

    // Reçoit l'accès aux comptes et l'enregistrement avec réessai.
    public PersonalNameChangeService(
        IUserAccountRepository accountRepository,
        DatabaseChangesSavingService savingService)
    {
        this.accountRepository = accountRepository;
        this.savingService = savingService;
    }

    // Enregistre le nom et le prénom tapés. Un champ vide, ou fait seulement d'espaces, efface ce qu'il y avait.
    public async Task<PersonalNameChangeResult> ChangeAsync(Guid userId, string? typedLastName, string? typedFirstName)
    {
        var lastName = PersonalNameRules.Normalize(typedLastName);
        var firstName = PersonalNameRules.Normalize(typedFirstName);
        var isLastNameInvalid = !PersonalNameRules.IsValid(lastName);
        var isFirstNameInvalid = !PersonalNameRules.IsValid(firstName);

        if (isLastNameInvalid || isFirstNameInvalid)
        {
            return new PersonalNameChangeResult.RefusedByRules
            {
                IsLastNameInvalid = isLastNameInvalid,
                IsFirstNameInvalid = isFirstNameInvalid
            };
        }

        return await savingService.SaveWithRetriesAsync(
            () => PrepareNewNameAsync(userId, lastName, firstName),
            new PersonalNameChangeResult.NotSaved());
    }

    // Un passage : lit le compte, puis prépare le nouveau nom et le nouveau prénom.
    private async Task<PreparedChanges<PersonalNameChangeResult>> PrepareNewNameAsync(Guid userId, string? lastName, string? firstName)
    {
        var account = await accountRepository.FindByIdAsync(userId);

        if (account == null)
        {
            return PreparedChanges<PersonalNameChangeResult>.NothingToSave(new PersonalNameChangeResult.NotSaved());
        }

        account.ReplaceName(lastName, firstName);

        return PreparedChanges<PersonalNameChangeResult>.SaveThenReturn(new PersonalNameChangeResult.Changed());
    }
}
