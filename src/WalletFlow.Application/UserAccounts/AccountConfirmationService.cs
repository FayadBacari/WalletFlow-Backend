using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.UserAccounts;

// Cas d'utilisation : un visiteur confirme son adresse avec le lien reçu et son mot de passe (GR Auth, SS §2).
// Il faut avoir à la fois la boîte mail et le mot de passe : ouvrir le lien seul n'active rien.
public class AccountConfirmationService
{
    private readonly IUserAccountRepository accountRepository;
    private readonly IDatabaseUnitOfWork unitOfWork;
    private readonly IPasswordHasher passwordHasher;
    private readonly ISecureTokenGenerator tokenGenerator;
    private readonly TimeProvider clock;

    // Reçoit les outils dont la confirmation a besoin.
    public AccountConfirmationService(
        IUserAccountRepository accountRepository,
        IDatabaseUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ISecureTokenGenerator tokenGenerator,
        TimeProvider clock)
    {
        this.accountRepository = accountRepository;
        this.unitOfWork = unitOfWork;
        this.passwordHasher = passwordHasher;
        this.tokenGenerator = tokenGenerator;
        this.clock = clock;
    }

    // Confirme le compte si le lien est encore valable et si le mot de passe est le bon.
    public async Task<AccountConfirmationResult> ConfirmAsync(
        string linkToken,
        string password)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var token = await FindUsableTokenAsync(linkToken, now);
        if (token == null)
        {
            return new AccountConfirmationResult.LinkNoLongerValid();
        }

        var account = await accountRepository.FindByIdAsync(token.UserId);
        if (account == null || account.IsEmailConfirmed)
        {
            return new AccountConfirmationResult.LinkNoLongerValid();
        }

        return await CheckPasswordThenConfirmAsync(account, token, password, now);
    }

    // Vérifie le mot de passe, puis confirme le compte. Un mauvais mot de passe ne consomme pas le lien.
    private async Task<AccountConfirmationResult> CheckPasswordThenConfirmAsync(
        User account,
        EmailToken token,
        string password,
        DateTime now)
    {
        var normalizedPassword = PasswordRules.Normalize(password);

        if (!await passwordHasher.VerifyAsync(normalizedPassword, account.PasswordHash))
        {
            return new AccountConfirmationResult.WrongPassword();
        }

        account.ConfirmEmail();
        token.MarkAsUsed(now);

        // Une nouvelle inscription a pu remplacer ce lien pendant la vérification du mot de passe :
        // l'enregistrement échoue alors, et rien n'est modifié. Il n'y a pas de second passage : le lien ne vaut plus.
        var saved = await unitOfWork.TrySaveChangesAsync();

        if (!saved)
        {
            return new AccountConfirmationResult.LinkNoLongerValid();
        }

        return new AccountConfirmationResult.AccountConfirmed();
    }

    // Cherche le jeton du lien. Renvoie null s'il n'existe pas, s'il a déjà servi ou s'il a expiré.
    private async Task<EmailToken?> FindUsableTokenAsync(string linkToken, DateTime now)
    {
        var tokenHash = tokenGenerator.HashToken(linkToken);
        var token = await accountRepository.FindTokenAsync(tokenHash, EmailTokenType.SignupConfirmation);

        if (token == null || !token.CanBeUsedAt(now))
        {
            return null;
        }

        return token;
    }
}
