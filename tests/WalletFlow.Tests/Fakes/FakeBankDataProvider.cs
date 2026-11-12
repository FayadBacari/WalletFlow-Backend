using WalletFlow.Application.BankConnections;

namespace WalletFlow.Tests.Fakes;

// Remplace le prestataire bancaire dans les tests : des banques, des sessions et des soldes écrits par le test, sans réseau.
// Le vrai client d'Enable Banking a ses propres tests, avec un faux serveur HTTP (EnableBankingClientTests).
public class FakeBankDataProvider : IBankDataProvider
{
    public const string BankName = "Banque de test";
    public const string ShortBankName = "Banque à 90 jours";
    public const string BankAddress = "https://banque.exemple.test/autorisation";

    private readonly object providerLock = new object();
    private int sessionNumber = 0;

    // Ce que le faux prestataire sait : ses banques, les sessions créées par chaque code, leurs comptes et leurs soldes.
    public List<ProvidedBank> Banks { get; } = new List<ProvidedBank>
    {
        new ProvidedBank(BankName, TimeSpan.FromDays(180)),
        new ProvidedBank(ShortBankName, TimeSpan.FromDays(90))
    };
    public Dictionary<string, ProvidedSession> SessionsByCode { get; } = new Dictionary<string, ProvidedSession>();
    public Dictionary<string, List<SessionAccount>> AccountsBySession { get; } = new Dictionary<string, List<SessionAccount>>();
    public Dictionary<string, List<ProvidedBalance>> BalancesByAccountUid { get; } = new Dictionary<string, List<ProvidedBalance>>();
    public HashSet<string> EndedSessions { get; } = new HashSet<string>();

    // Ce que les tests vérifient : les autorisations demandées, les sessions révoquées, et la présence transmise à chaque lecture.
    public List<BankAuthorizationRequest> AuthorizationRequests { get; } = new List<BankAuthorizationRequest>();
    public List<string> RevokedSessions { get; } = new List<string>();
    public List<UserPresence?> BalanceReadPresences { get; } = new List<UserPresence?>();
    public int UsedCodeCount { get; private set; } = 0;

    // Simule une panne de tout le prestataire.
    public bool IsDown { get; set; } = false;

    // Simule une lecture de soldes refusée pour cette raison.
    public BankProviderFailure? BalanceFailure { get; set; } = null;

    // Simule une révocation que le prestataire ne confirme pas.
    public bool RevocationFails { get; set; } = false;

    // La fin d'autorisation des prochaines sessions. Sans réglage : 180 jours après l'heure réelle, moins la marge d'horloge.
    public DateTime? SessionValidUntil { get; set; } = null;

    // Une action jouée juste avant chaque lecture de soldes : un test y simule une autre requête au même instant.
    public Action? BeforeBalanceRead { get; set; } = null;

    // Crée une session pour ce code, avec un compte par élément. Les comptes ont aussi leurs soldes et leur session.
    public ProvidedSession AddSession(string code, params TestBankAccount[] accounts)
    {
        lock (providerLock)
        {
            sessionNumber = sessionNumber + 1;
            var sessionId = "session-" + sessionNumber + "-" + Guid.NewGuid().ToString("N");
            var providedAccounts = new List<ProvidedAccount>();
            var sessionAccounts = new List<SessionAccount>();

            foreach (var account in accounts)
            {
                var uid = "compte-" + sessionNumber + "-" + account.StableId;
                providedAccounts.Add(new ProvidedAccount(uid, account.StableId, account.Name, account.Currency));
                sessionAccounts.Add(new SessionAccount(uid, account.StableId));
                BalancesByAccountUid[uid] = new List<ProvidedBalance>
                {
                    new ProvidedBalance("ITAV", account.BalanceInCents + 1000, account.Currency),
                    new ProvidedBalance("CLBD", account.BalanceInCents, account.Currency)
                };
            }

            var validUntil = SessionValidUntil ?? DateTime.UtcNow.AddDays(180).AddMinutes(-10);
            var session = new ProvidedSession(sessionId, validUntil, providedAccounts);
            SessionsByCode[code] = session;
            AccountsBySession[sessionId] = sessionAccounts;

            return session;
        }
    }

    // Change le solde comptable d'un compte, dans toutes les sessions où il se trouve.
    public void ChangeBalance(string stableId, long newBalanceInCents)
    {
        lock (providerLock)
        {
            foreach (var session in AccountsBySession.Values)
            {
                foreach (var account in session.Where(account => account.StableId == stableId))
                {
                    BalancesByAccountUid[account.Uid] = new List<ProvidedBalance> { new ProvidedBalance("CLBD", newBalanceInCents, "EUR") };
                }
            }
        }
    }

    // Donne les banques, ou échoue si le prestataire est en panne.
    public Task<List<ProvidedBank>> ListBanksAsync()
    {
        lock (providerLock)
        {
            ThrowIfDown();
            return Task.FromResult(new List<ProvidedBank>(Banks));
        }
    }

    // Note la demande d'autorisation, et renvoie l'adresse de la fausse banque.
    public Task<string> StartAuthorizationAsync(BankAuthorizationRequest request)
    {
        lock (providerLock)
        {
            ThrowIfDown();
            AuthorizationRequests.Add(request);
            return Task.FromResult(BankAddress);
        }
    }

    // Donne la session de ce code. Un code inconnu, ou déjà utilisé, est refusé comme chez le vrai prestataire.
    public Task<ProvidedSession> CreateSessionAsync(string code)
    {
        lock (providerLock)
        {
            ThrowIfDown();

            if (!SessionsByCode.Remove(code, out var session))
            {
                throw new BankProviderException(BankProviderFailure.AuthorizationRefused, "Code inconnu ou déjà utilisé.");
            }

            UsedCodeCount = UsedCodeCount + 1;
            return Task.FromResult(session);
        }
    }

    // Donne les comptes d'une session encore autorisée.
    public Task<List<SessionAccount>> ReadSessionAccountsAsync(string sessionId)
    {
        lock (providerLock)
        {
            ThrowIfDown();

            if (EndedSessions.Contains(sessionId) || RevokedSessions.Contains(sessionId) || !AccountsBySession.TryGetValue(sessionId, out var accounts))
            {
                throw new BankProviderException(BankProviderFailure.SessionEnded, "Session finie.");
            }

            return Task.FromResult(new List<SessionAccount>(accounts));
        }
    }

    // Donne les soldes d'un compte, et note si l'utilisateur était là.
    public Task<List<ProvidedBalance>> ReadBalancesAsync(string accountUid, UserPresence? presence)
    {
        lock (providerLock)
        {
            ThrowIfDown();
            BalanceReadPresences.Add(presence);
            BeforeBalanceRead?.Invoke();

            if (BalanceFailure is BankProviderFailure failure)
            {
                throw new BankProviderException(failure, "Lecture des soldes refusée par le test.");
            }

            return Task.FromResult(new List<ProvidedBalance>(BalancesByAccountUid.GetValueOrDefault(accountUid) ?? new List<ProvidedBalance>()));
        }
    }

    // Note la session révoquée, ou échoue si le test le demande.
    public Task RevokeSessionAsync(string sessionId)
    {
        lock (providerLock)
        {
            if (IsDown || RevocationFails)
            {
                throw new BankProviderException(BankProviderFailure.Unavailable, "Révocation non confirmée.");
            }

            RevokedSessions.Add(sessionId);
            return Task.CompletedTask;
        }
    }

    // Échoue comme un prestataire en panne.
    private void ThrowIfDown()
    {
        if (IsDown)
        {
            throw new BankProviderException(BankProviderFailure.Unavailable, "Prestataire en panne.");
        }
    }
}

// Un compte d'une fausse session : son identifiant stable, son nom, sa devise et son solde comptable.
public record TestBankAccount(string StableId, string? Name, string Currency, long BalanceInCents);
