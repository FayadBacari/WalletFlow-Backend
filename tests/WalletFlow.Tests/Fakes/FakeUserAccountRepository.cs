using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Fakes;

// Remplace la vraie base dans les tests : garde les comptes et leurs liens en mémoire.
// Comme EF Core, il rend les objets gardés, que les règles des entités changent directement. Il garde aussi une copie
// de chaque objet lu : si l'enregistrement échoue, tout reprend ses valeurs, comme après une transaction annulée.
public class FakeUserAccountRepository : IUserAccountRepository, ITransactionalFakeRepository
{
    public const string FailureDetail = "detail-technique-de-la-panne-simulee";

    private readonly object pendingChangesLock = new object();
    private readonly ReadCopies<User> readAccounts = new ReadCopies<User>();
    private readonly ReadCopies<EmailToken> readTokens = new ReadCopies<EmailToken>();
    private readonly List<User> accountsUnderCheck = new List<User>();
    private readonly List<EmailToken> preparedSignupTokens = new List<EmailToken>();
    private readonly List<Guid> accountsLosingOldAddressLinks = new List<Guid>();
    private readonly List<Guid> accountsLosingAllLinks = new List<Guid>();
    private readonly List<User> accountsToErase = new List<User>();
    private Action? concurrentChanges = null;

    // Ce que la fausse base contient.
    public List<User> SavedUsers { get; } = new List<User>();
    public List<Category> SavedCategories { get; } = new List<Category>();
    public List<EmailToken> SavedEmailTokens { get; } = new List<EmailToken>();

    // Ce que les tests vérifient : les passages du nettoyage, les recherches par adresse et les essais enregistrés.
    public List<DateTime> CleanupDates { get; } = new List<DateTime>();
    public int EmailSearchCount { get; private set; } = 0;
    public int LoginCounterSaveCount { get; private set; } = 0;

    // Simule une panne de la base : chaque recherche d'adresse échoue avec ce détail technique.
    public bool IsDown { get; set; } = false;

    // Simule une panne de la base pendant l'effacement des comptes non confirmés.
    public bool FailsDuringCleanup { get; set; } = false;

    // Simule une panne de la base au moment de lire un lien.
    public bool FailsWhenReadingLinks { get; set; } = false;

    // Les réglages qui suivent simulent une autre requête qui agit juste avant l'enregistrement de celle du test.

    // Une autre inscription prend la même adresse juste après la recherche.
    public User? ConcurrentAccount { get; set; } = null;

    // Une confirmation arrive juste avant le remplacement du mot de passe.
    public bool ConfirmBeforePasswordChange { get; set; } = false;

    // Une nouvelle inscription remplace le lien juste avant la confirmation.
    public bool ReplaceTokenBeforeConfirmation { get; set; } = false;

    // Une autre requête utilise le même lien juste avant l'enregistrement.
    public bool UseLinkBeforeSave { get; set; } = false;

    // D'autres requêtes changent le compte juste avant l'utilisation d'un lien de déblocage ou de récupération : un conflit chacune.
    public int LinkUseConflicts { get; set; } = 0;

    // D'autres requêtes comptent leur essai juste avant l'enregistrement : un conflit chacune.
    public int LoginCounterConflicts { get; set; } = 0;

    // D'autres requêtes comptent leur propre essai du mot de passe sur le même lien du support juste avant l'enregistrement :
    // un conflit chacune, et un essai de plus sur le lien (DD §4.2).
    public int SupportLinkTryConflicts { get; set; } = 0;

    // Le compte est effacé pendant la connexion : au-delà de ce nombre de recherches par identifiant, il n'existe plus.
    public int? SearchesBeforeAccountDisappears { get; set; } = null;

    // Une connexion annule la suppression d'un compte que cet enregistrement contrôle, ou efface, juste avant lui.
    public bool DeletionCancelledBeforeCheckedSave { get; set; } = false;

    // Un autre compte prend la nouvelle adresse juste avant l'enregistrement d'un changement d'adresse.
    public User? AccountTakingNewEmailBeforeSave { get; set; } = null;

    // Une récupération donne ce nouveau mot de passe à un compte juste avant un enregistrement qui le contrôle
    // (voir PrepareUnchangedCheck). Un compte qui n'est ni changé ni contrôlé n'est pas réécrit : il ne voit pas ce conflit.
    public string? PasswordChangedBeforeCheckedSave { get; set; } = null;

    // Cherche en mémoire le compte qui utilise cette adresse.
    public Task<User?> FindByEmailAsync(string email)
    {
        if (IsDown)
        {
            throw new InvalidOperationException(FailureDetail);
        }

        EmailSearchCount = EmailSearchCount + 1;

        foreach (var user in SavedUsers)
        {
            if (user.Email == email)
            {
                KeepCopy(user);
                return Task.FromResult<User?>(user);
            }
        }

        return Task.FromResult<User?>(null);
    }

    // Cherche en mémoire un compte par son identifiant.
    public Task<User?> FindByIdAsync(Guid accountId)
    {
        if (SearchesBeforeAccountDisappears == 0)
        {
            return Task.FromResult<User?>(null);
        }

        SearchesBeforeAccountDisappears = SearchesBeforeAccountDisappears - 1;

        foreach (var user in SavedUsers)
        {
            if (user.Id == accountId)
            {
                KeepCopy(user);
                return Task.FromResult<User?>(user);
            }
        }

        return Task.FromResult<User?>(null);
    }

    // Cherche en mémoire un jeton de ce type par son empreinte.
    public Task<EmailToken?> FindTokenAsync(string tokenHash, EmailTokenType tokenType)
    {
        if (FailsWhenReadingLinks)
        {
            throw new InvalidOperationException(FailureDetail);
        }

        foreach (var token in SavedEmailTokens)
        {
            if (token.TokenHash == tokenHash
                && token.Type == tokenType)
            {
                KeepCopy(token);
                return Task.FromResult<EmailToken?>(token);
            }
        }

        return Task.FromResult<EmailToken?>(null);
    }

    // Garde tout de suite le nouveau compte en mémoire, sauf si l'adresse vient d'être prise.
    public Task<bool> TryAddAccountAsync(
        User newAccount,
        List<Category> categories,
        EmailToken confirmationToken)
    {
        if (ConcurrentAccount != null)
        {
            SavedUsers.Add(ConcurrentAccount);
            return Task.FromResult(false);
        }

        SavedUsers.Add(newAccount);
        SavedCategories.AddRange(categories);
        SavedEmailTokens.Add(confirmationToken);
        return Task.FromResult(true);
    }

    // Garde tout de suite le nouveau jeton en mémoire, et retire l'ancien du même type et du même compte.
    public Task ReplaceTokenAsync(EmailToken newToken)
    {
        SavedEmailTokens.RemoveAll(token =>
            token.UserId == newToken.UserId
            && token.Type == newToken.Type);
        SavedEmailTokens.Add(newToken);
        return Task.CompletedTask;
    }

    // Garde de côté le nouveau lien de confirmation : il ne remplacera les anciens qu'avec les autres changements.
    public Task PrepareConfirmationTokenReplacementAsync(EmailToken newToken)
    {
        lock (pendingChangesLock)
        {
            preparedSignupTokens.Add(newToken);
        }

        return Task.CompletedTask;
    }

    // Garde de côté l'effacement des liens partis vers l'ancienne adresse : il ne se fera qu'avec les autres changements.
    public Task PrepareOldAddressLinksRemovalAsync(Guid accountId)
    {
        lock (pendingChangesLock)
        {
            accountsLosingOldAddressLinks.Add(accountId);
        }

        return Task.CompletedTask;
    }

    // Note le compte qui perdra tous ses liens à l'enregistrement.
    public Task PrepareAllLinksRemovalAsync(Guid accountId)
    {
        lock (pendingChangesLock)
        {
            accountsLosingAllLinks.Add(accountId);
        }

        return Task.CompletedTask;
    }

    // Donne les comptes dont la suppression est demandée à cette date ou avant, du plus ancien au plus récent.
    public Task<List<Guid>> FindAccountsToEraseAsync(DateTime requestedAtOrBefore)
    {
        lock (pendingChangesLock)
        {
            var accountIds = SavedUsers
                .Where(user => user.DeletionRequestedAt != null && user.DeletionRequestedAt <= requestedAtOrBefore)
                .OrderBy(user => user.DeletionRequestedAt)
                .ThenBy(user => user.Id)
                .Select(user => user.Id)
                .ToList();
            return Task.FromResult(accountIds);
        }
    }

    // Note le compte à effacer à l'enregistrement. Il est contrôlé comme dans la vraie base : les conflits simulés le visent.
    public void PrepareErasure(User account)
    {
        lock (pendingChangesLock)
        {
            accountsToErase.Add(account);
            accountsUnderCheck.Add(account);
        }
    }

    // Note le compte à contrôler : les conflits simulés par les réglages de ce faux dépôt le visent.
    public void PrepareUnchangedCheck(User account)
    {
        lock (pendingChangesLock)
        {
            accountsUnderCheck.Add(account);
        }
    }

    // Garde la date du passage. La vraie règle d'effacement est testée sur la vraie base.
    public Task<int> DeleteExpiredUnconfirmedAccountsAsync(DateTime now)
    {
        if (FailsDuringCleanup)
        {
            throw new InvalidOperationException(FailureDetail);
        }

        CleanupDates.Add(now);
        return Task.FromResult(0);
    }

    // Aucune table de ce faux dépôt ne tombe en panne pendant un enregistrement.
    public void ThrowIfTableIsDown()
    {
    }

    // Décide si une autre requête change les mêmes lignes juste avant l'enregistrement, selon les réglages du test,
    // et prépare ce que cette autre requête aura écrit.
    public bool HasConcurrentChange()
    {
        lock (pendingChangesLock)
        {
            var usedToken = FindTokenUsedSinceRead();
            var countedAccount = FindAccountCountedSinceRead();

            // Une confirmation passe avant le nouveau mot de passe d'une nouvelle inscription.
            if (preparedSignupTokens.Count > 0 && ConfirmBeforePasswordChange)
            {
                var accountId = preparedSignupTokens[0].UserId;
                concurrentChanges = () => ConfirmLikeOtherRequest(accountId);
                return true;
            }

            // Une nouvelle inscription remplace le lien de confirmation qui vient de servir.
            if (usedToken != null && usedToken.Type == EmailTokenType.SignupConfirmation && ReplaceTokenBeforeConfirmation)
            {
                return true;
            }

            // Une autre requête compte son propre essai sur le même lien du support : le lien a changé de version.
            var triedToken = readTokens.FindChangedSinceRead((copy, current) => copy.PasswordTryCount != current.PasswordTryCount);
            if (triedToken != null && SupportLinkTryConflicts > 0)
            {
                SupportLinkTryConflicts = SupportLinkTryConflicts - 1;
                concurrentChanges = () => triedToken.CountPasswordTry();
                return true;
            }

            // Une autre requête utilise le même lien, à la même date.
            if (usedToken != null && usedToken.UsedAt is DateTime usedAt && UseLinkBeforeSave)
            {
                concurrentChanges = () => usedToken.MarkAsUsed(usedAt);
                return true;
            }

            // Une autre requête change le compte du lien qui vient de servir.
            if (usedToken != null && LinkUseConflicts > 0)
            {
                LinkUseConflicts = LinkUseConflicts - 1;
                return true;
            }

            // Une autre requête compte son propre essai de connexion sur le même compte.
            if (countedAccount != null && LoginCounterConflicts > 0)
            {
                LoginCounterConflicts = LoginCounterConflicts - 1;
                concurrentChanges = () => CountAttemptLikeOtherRequest(countedAccount);
                return true;
            }

            // Une récupération change le mot de passe d'un compte que cet enregistrement contrôle, juste avant lui.
            if (PasswordChangedBeforeCheckedSave is string newPasswordHash && accountsUnderCheck.Count > 0)
            {
                var checkedAccount = accountsUnderCheck[0];
                PasswordChangedBeforeCheckedSave = null;
                concurrentChanges = () => checkedAccount.ReplacePasswordHash(newPasswordHash);
                return true;
            }

            // Une connexion annule la suppression du compte contrôlé juste avant l'enregistrement : la version du compte a changé.
            if (DeletionCancelledBeforeCheckedSave && accountsUnderCheck.Count > 0)
            {
                var checkedAccount = accountsUnderCheck[0];
                DeletionCancelledBeforeCheckedSave = false;
                concurrentChanges = () => checkedAccount.CancelDeletion();
                return true;
            }

            // Un autre compte prend la nouvelle adresse juste avant l'enregistrement du changement : la base refuserait l'adresse en double.
            var otherAccount = AccountTakingNewEmailBeforeSave;
            if (otherAccount != null && FindAccountWithNewEmailSinceRead() != null)
            {
                AccountTakingNewEmailBeforeSave = null;
                concurrentChanges = () => SavedUsers.Add(otherAccount);
                return true;
            }

            return false;
        }
    }

    // Remet les comptes et les liens lus comme ils étaient, et oublie le lien préparé.
    public void Rollback()
    {
        lock (pendingChangesLock)
        {
            readAccounts.RestoreAll();
            readTokens.RestoreAll();
            ClearTracking();
        }
    }

    // Écrit ce que l'autre requête simulée a enregistré.
    public void ApplyConcurrentChanges()
    {
        var changes = concurrentChanges;
        concurrentChanges = null;

        if (changes != null)
        {
            changes();
        }
    }

    // Garde les changements : le nouveau lien de confirmation remplace les anciens, et l'essai compté est noté.
    public void Commit()
    {
        lock (pendingChangesLock)
        {
            if (FindAccountCountedSinceRead() != null)
            {
                LoginCounterSaveCount = LoginCounterSaveCount + 1;
            }

            foreach (var newSignupToken in preparedSignupTokens)
            {
                SavedEmailTokens.RemoveAll(token =>
                    token.UserId == newSignupToken.UserId
                    && token.Type == EmailTokenType.SignupConfirmation);
                SavedEmailTokens.Add(newSignupToken);
            }

            foreach (var accountId in accountsLosingOldAddressLinks)
            {
                SavedEmailTokens.RemoveAll(token =>
                    token.UserId == accountId
                    && (token.Type == EmailTokenType.PasswordRecovery
                        || token.Type == EmailTokenType.AccountUnlock
                        || token.Type == EmailTokenType.TwoFactorDeactivation));
            }

            foreach (var accountId in accountsLosingAllLinks)
            {
                SavedEmailTokens.RemoveAll(token => token.UserId == accountId);
            }

            // Le compte effacé part avec ses liens, comme ON DELETE CASCADE dans la vraie base.
            foreach (var account in accountsToErase)
            {
                SavedUsers.Remove(account);
                SavedEmailTokens.RemoveAll(token => token.UserId == account.Id);
            }

            ClearTracking();
        }
    }

    // Garde une copie du compte tel qu'il est lu : il reprendra ces valeurs si l'enregistrement échoue.
    private void KeepCopy(User account)
    {
        lock (pendingChangesLock)
        {
            readAccounts.Keep(account);
        }
    }

    // Garde une copie du lien tel qu'il est lu : il reprendra ces valeurs si l'enregistrement échoue.
    private void KeepCopy(EmailToken token)
    {
        lock (pendingChangesLock)
        {
            readTokens.Keep(token);
        }
    }

    // Donne le lien qui a servi depuis sa lecture, ou null.
    private EmailToken? FindTokenUsedSinceRead()
    {
        return readTokens.FindChangedSinceRead((copy, token) => copy.UsedAt == null && token.UsedAt != null);
    }

    // Donne le compte dont un essai de connexion a été compté depuis sa lecture, ou null.
    // Une connexion réussie ou un déblocage remettent le compteur à zéro : ce ne sont pas des essais comptés.
    private User? FindAccountCountedSinceRead()
    {
        return readAccounts.FindChangedSinceRead((copy, account) =>
            account.FailedLoginAttemptCount != copy.FailedLoginAttemptCount
            && account.FailedLoginAttemptCount > 0);
    }

    // Donne le compte dont l'adresse a changé depuis sa lecture, ou null.
    private User? FindAccountWithNewEmailSinceRead()
    {
        return readAccounts.FindChangedSinceRead((copy, account) => account.Email != copy.Email);
    }

    // L'autre requête a confirmé ce compte.
    private void ConfirmLikeOtherRequest(Guid accountId)
    {
        foreach (var user in SavedUsers)
        {
            if (user.Id == accountId)
            {
                user.ConfirmEmail();
            }
        }
    }

    // L'autre requête a compté son propre essai sur ce compte : un essai de plus que ce que cette requête avait lu.
    private static void CountAttemptLikeOtherRequest(User account)
    {
        EntityValues.WriteProperty(
            account,
            nameof(User.FailedLoginAttemptCount),
            (short)(account.FailedLoginAttemptCount + 1));
    }

    // Oublie les copies et ce qui était préparé : le prochain passage repart de ce qui est gardé.
    private void ClearTracking()
    {
        readAccounts.Clear();
        readTokens.Clear();
        preparedSignupTokens.Clear();
        accountsLosingOldAddressLinks.Clear();
        accountsLosingAllLinks.Clear();
        accountsToErase.Clear();
        accountsUnderCheck.Clear();
    }
}
