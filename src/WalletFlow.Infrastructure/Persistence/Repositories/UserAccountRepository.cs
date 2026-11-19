using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.Repositories;

// Lit et écrit les comptes des utilisateurs et leurs liens dans la base PostgreSQL.
// Les objets lus ici sont suivis par EF Core : l'unité de travail enregistre ce que leurs règles ont changé (voir IDatabaseUnitOfWork).
public class UserAccountRepository : IUserAccountRepository
{
    private readonly WalletFlowDatabaseContext databaseContext;

    // Reçoit l'accès à la base de la requête en cours.
    public UserAccountRepository(WalletFlowDatabaseContext databaseContext)
    {
        this.databaseContext = databaseContext;
    }

    // Cherche le compte qui utilise cette adresse e-mail. Renvoie null s'il n'y en a pas.
    public async Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await databaseContext.Users.SingleOrDefaultAsync(user =>
            user.Email == email,
            cancellationToken);
    }

    // Cherche un compte par son identifiant. Renvoie null s'il n'existe plus.
    public async Task<User?> FindByIdAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return await databaseContext.Users.SingleOrDefaultAsync(user =>
            user.Id == accountId,
            cancellationToken);
    }

    // Cherche un jeton de ce type par son empreinte. Renvoie null s'il n'existe pas : un lien ne sert jamais à un autre usage.
    public async Task<EmailToken?> FindTokenAsync(string tokenHash, EmailTokenType tokenType, CancellationToken cancellationToken = default)
    {
        return await databaseContext.EmailTokens.SingleOrDefaultAsync(token =>
            token.TokenHash == tokenHash
            && token.Type == tokenType,
            cancellationToken);
    }

    // Efface l'ancien jeton du même type et du même compte, puis enregistre le nouveau : l'ancien lien ne marche plus (SS §2),
    // et il ne reste jamais qu'un jeton par type et par compte. L'effacement est direct : un clic sur l'ancien lien au même instant
    // ne peut pas le faire échouer. Les deux écritures forment une seule transaction SQL : si l'enregistrement du nouveau jeton
    // échoue, l'ancien n'est pas effacé, et le compte n'est jamais laissé sans lien. Cette écriture est immédiate :
    // elle refuse de partir si des changements attendent l'unité de travail, pour ne jamais les emporter avec elle.
    public async Task ReplaceTokenAsync(EmailToken newToken, CancellationToken cancellationToken = default)
    {
        databaseContext.ThrowIfChangesArePending();

        // Une transaction déjà ouverte par l'appelant couvre les deux écritures : EF Core refuse d'en ouvrir une seconde.
        if (databaseContext.Database.CurrentTransaction != null)
        {
            await DeleteOldThenAddTokenAsync(newToken, cancellationToken);
            return;
        }

        await using var transaction = await databaseContext.Database.BeginTransactionAsync(cancellationToken);
        await DeleteOldThenAddTokenAsync(newToken, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // Efface l'ancien jeton du même type et du même compte, puis enregistre le nouveau, dans la transaction en cours.
    private async Task DeleteOldThenAddTokenAsync(EmailToken newToken, CancellationToken cancellationToken = default)
    {
        await databaseContext.EmailTokens
            .Where(token =>
                token.UserId == newToken.UserId
                && token.Type == newToken.Type)
            .ExecuteDeleteAsync(cancellationToken);

        databaseContext.EmailTokens.Add(newToken);

        await databaseContext.SaveChangesAsync(cancellationToken);
    }

    // Enregistre le nouvel utilisateur, ses catégories et son jeton en une seule transaction SQL :
    // SaveChangesAsync enregistre tout, ou rien (MPD §3).
    // Renvoie faux si une autre inscription vient de prendre la même adresse.
    public async Task<bool> TryAddAccountAsync(
        User newAccount,
        List<Category> categories,
        EmailToken confirmationToken,
        CancellationToken cancellationToken = default)
    {
        databaseContext.Users.Add(newAccount);
        databaseContext.Categories.AddRange(categories);
        databaseContext.EmailTokens.Add(confirmationToken);

        try
        {
            await databaseContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
        {
            if (!PostgresErrors.IsEmailAlreadyUsed(exception))
            {
                throw;
            }

            databaseContext.ChangeTracker.Clear();
            return false;
        }
    }

    // Prépare le remplacement du lien de confirmation d'un compte : ses anciens liens de confirmation seront effacés, et le nouveau enregistré.
    // EF Core suit les anciens liens : si l'un d'eux vient de servir à confirmer le compte, l'enregistrement échoue.
    public async Task PrepareConfirmationTokenReplacementAsync(EmailToken newToken, CancellationToken cancellationToken = default)
    {
        var oldTokens = await databaseContext.EmailTokens
            .Where(token =>
                token.UserId == newToken.UserId
                && token.Type == EmailTokenType.SignupConfirmation)
            .ToListAsync(cancellationToken);

        databaseContext.EmailTokens.RemoveRange(oldTokens);
        databaseContext.EmailTokens.Add(newToken);
    }

    // Prépare l'effacement des liens de récupération, de déblocage et de désactivation de la double authentification du compte :
    // ils sont partis vers l'ancienne adresse (SS §2).
    // EF Core suit ces liens : si l'un d'eux vient de servir ou d'être remplacé, l'enregistrement échoue et le passage recommence.
    public async Task PrepareOldAddressLinksRemovalAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var links = await databaseContext.EmailTokens
            .Where(token =>
                token.UserId == accountId
                && (token.Type == EmailTokenType.PasswordRecovery
                    || token.Type == EmailTokenType.AccountUnlock
                    || token.Type == EmailTokenType.TwoFactorDeactivation))
            .ToListAsync(cancellationToken);

        databaseContext.EmailTokens.RemoveRange(links);
    }

    // Le compte sera écrit même si rien n'y a changé : cette écriture contrôle en base la version de la ligne,
    // et elle échoue si une autre requête a changé le compte depuis sa lecture. EF Core n'écrit une ligne que si une colonne
    // a changé : le compteur d'essais est donc réécrit avec sa propre valeur.
    public void PrepareUnchangedCheck(User account)
    {
        databaseContext.Entry(account).Property(nameof(User.FailedLoginAttemptCount)).IsModified = true;
    }

    // Efface les comptes non confirmés qui n'ont plus aucun lien valable, et renvoie leur nombre.
    // Leurs catégories et leurs jetons partent avec eux (ON DELETE CASCADE).
    public async Task<int> DeleteExpiredUnconfirmedAccountsAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        return await databaseContext.Users
            .Where(user =>
                !user.IsEmailConfirmed
                && !databaseContext.EmailTokens.Any(token =>
                    token.UserId == user.Id
                    && token.Type == EmailTokenType.SignupConfirmation
                    && token.ExpiresAt > now))
            .ExecuteDeleteAsync(cancellationToken);
    }

    // Prépare l'effacement de tous les liens du compte. EF Core les suit : si l'un d'eux vient de servir ou d'être remplacé,
    // l'enregistrement échoue et le passage recommence.
    public async Task PrepareAllLinksRemovalAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var links = await databaseContext.EmailTokens
            .Where(token => token.UserId == accountId)
            .ToListAsync(cancellationToken);

        databaseContext.EmailTokens.RemoveRange(links);
    }

    // Lit les comptes dont la suppression est demandée à cette date ou avant.
    public async Task<List<Guid>> FindAccountsToEraseAsync(DateTime requestedAtOrBefore, CancellationToken cancellationToken = default)
    {
        return await databaseContext.Users
            .AsNoTracking()
            .Where(user => user.DeletionRequestedAt != null && user.DeletionRequestedAt <= requestedAtOrBefore)
            .OrderBy(user => user.DeletionRequestedAt)
            .ThenBy(user => user.Id)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
    }

    // Lit seulement les identifiants : chaque compte est ensuite relu et rechiffré dans son propre passage.
    public async Task<List<Guid>> FindAccountsWithOtherTotpKeyAsync(
        string currentKeyPrefix,
        Guid? afterUserId,
        int count,
        CancellationToken cancellationToken = default)
    {
        var users = databaseContext.Users
            .AsNoTracking()
            .Where(user => user.EncryptedTotpSecret != null && !user.EncryptedTotpSecret.StartsWith(currentKeyPrefix));

        if (afterUserId is Guid lastUserId)
        {
            users = users.Where(user => user.Id.CompareTo(lastUserId) > 0);
        }

        return await users
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    // EF Core efface la ligne du compte en vérifiant sa version (xmin) : une connexion qui vient d'annuler la suppression fait
    // échouer l'effacement. La base efface tout ce qui appartient au compte (ON DELETE CASCADE), et son déclencheur retire
    // l'identifiant et l'adresse IP de ses événements du journal.
    public void PrepareErasure(User account)
    {
        databaseContext.Users.Remove(account);
    }
}
