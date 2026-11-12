using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WalletFlow.Application.BankConnections;

namespace WalletFlow.Infrastructure.BankData;

// Le prestataire Enable Banking, agréé dans l'Union européenne, en lecture seule (GR Comptes, SS §7).
// Chaque requête porte un jeton signé par la clé privée de WalletFlow (voir EnableBankingTokenSigner).
// Le journal technique nomme la route et l'erreur, jamais un code, un state, une session ou un compte (SS §7).
public class EnableBankingClient : IBankDataProvider
{
    public const string Country = "FR";
    public const string PersonalAccounts = "personal";
    public const string AuthorizedStatus = "AUTHORIZED";
    public static readonly Uri ApiAddress = new Uri("https://api.enablebanking.com/");
    public static readonly TimeSpan BankListLifetime = TimeSpan.FromHours(6);

    // Montant le plus grand lu en texte : bien au-delà des limites de la SS §5, assez petit pour ne jamais déborder.
    private const decimal LargestReadAmount = 1_000_000_000_000m;

    // Longueur la plus grande du navigateur transmis à la banque.
    public const int MaximumHeaderLength = 512;

    // Les erreurs d'Enable Banking qui disent que la session ne sert plus : il faut une nouvelle autorisation.
    private static readonly List<string> SessionEndedErrors = new List<string>
    {
        "EXPIRED_SESSION", "REVOKED_SESSION", "CLOSED_SESSION", "SESSION_DOES_NOT_EXIST", "WRONG_SESSION_STATUS"
    };

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient httpClient;
    private readonly EnableBankingTokenSigner tokenSigner;
    private readonly TimeProvider clock;
    private readonly ILogger<EnableBankingClient> logger;
    private readonly object bankListLock = new object();
    private List<ProvidedBank>? bankList = null;
    private DateTimeOffset bankListExpiresAt;

    // Reçoit le client HTTP déjà dirigé vers l'API d'Enable Banking, le signataire des jetons, l'horloge et le journal technique.
    public EnableBankingClient(
        HttpClient httpClient,
        EnableBankingTokenSigner tokenSigner,
        TimeProvider clock,
        ILogger<EnableBankingClient> logger)
    {
        this.httpClient = httpClient;
        this.tokenSigner = tokenSigner;
        this.clock = clock;
        this.logger = logger;
    }

    // Donne les banques françaises qui permettent de lire les comptes de particuliers. La liste est gardée 6 heures.
    public async Task<List<ProvidedBank>> ListBanksAsync()
    {
        var now = clock.GetUtcNow();

        lock (bankListLock)
        {
            if (bankList != null && now < bankListExpiresAt)
            {
                return new List<ProvidedBank>(bankList);
            }
        }

        var response = await SendAsync<AspspListResponse>(
            HttpMethod.Get, "aspsps?country=" + Country + "&psu_type=" + PersonalAccounts + "&service=AIS", null, null, "aspsps");
        var banks = new List<ProvidedBank>();

        foreach (var bank in response.Aspsps ?? new List<AspspData>())
        {
            if (!string.IsNullOrWhiteSpace(bank.Name) && bank.Country == Country && bank.MaximumConsentValidity > 0)
            {
                banks.Add(new ProvidedBank(bank.Name, TimeSpan.FromSeconds(bank.MaximumConsentValidity.Value)));
            }
        }

        lock (bankListLock)
        {
            bankList = banks;
            bankListExpiresAt = now + BankListLifetime;
        }

        return new List<ProvidedBank>(banks);
    }

    // Commence l'autorisation : la banque renverra l'utilisateur à l'adresse fixe du site, avec le state (SS §7).
    public async Task<string> StartAuthorizationAsync(BankAuthorizationRequest request)
    {
        var body = new StartAuthorizationBody(
            new AccessBody(new DateTimeOffset(request.ValidUntil, TimeSpan.Zero)),
            new AspspBody(request.BankName, Country),
            request.State,
            request.ReturnAddress,
            PersonalAccounts);

        var response = await SendAsync<StartAuthorizationResponse>(HttpMethod.Post, "auth", body, null, "auth");

        if (!Uri.TryCreate(response.Url, UriKind.Absolute, out var bankAddress) || bankAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw Unavailable("auth", "Enable Banking n'a pas donné d'adresse HTTPS pour la banque.");
        }

        return bankAddress.AbsoluteUri;
    }

    // Termine l'autorisation avec le code renvoyé par la banque. Un code refusé lève AuthorizationRefused.
    public async Task<ProvidedSession> CreateSessionAsync(string code)
    {
        var response = await SendAsync<AuthorizeSessionResponse>(HttpMethod.Post, "sessions", new AuthorizeSessionBody(code), null, "sessions");

        if (string.IsNullOrWhiteSpace(response.SessionId) || response.Access?.ValidUntil is not DateTimeOffset validUntil)
        {
            throw Unavailable("sessions", "Enable Banking n'a pas donné de session complète.");
        }

        var accounts = new List<ProvidedAccount>();

        foreach (var account in response.Accounts ?? new List<AccountResource>())
        {
            if (string.IsNullOrWhiteSpace(account.Uid) || string.IsNullOrWhiteSpace(account.IdentificationHash))
            {
                continue;
            }

            accounts.Add(new ProvidedAccount(
                account.Uid,
                ComputeStableId(account.IdentificationHash),
                account.Details ?? account.Product,
                account.Currency ?? string.Empty));
        }

        return new ProvidedSession(response.SessionId, validUntil.UtcDateTime, accounts);
    }

    // Donne les comptes d'une session. Une session qui n'est plus autorisée lève SessionEnded.
    public async Task<List<SessionAccount>> ReadSessionAccountsAsync(string sessionId)
    {
        var response = await SendAsync<SessionResponse>(HttpMethod.Get, "sessions/" + Uri.EscapeDataString(sessionId), null, null, "sessions/{id}");

        if (response.Status != AuthorizedStatus)
        {
            throw new BankProviderException(BankProviderFailure.SessionEnded, "La session n'est plus autorisée.");
        }

        var accounts = new List<SessionAccount>();

        foreach (var account in response.AccountsData ?? new List<SessionAccountData>())
        {
            if (!string.IsNullOrWhiteSpace(account.Uid) && !string.IsNullOrWhiteSpace(account.IdentificationHash))
            {
                accounts.Add(new SessionAccount(account.Uid, ComputeStableId(account.IdentificationHash)));
            }
        }

        return accounts;
    }

    // Donne les soldes d'un compte. Quand l'utilisateur est là, ses en-têtes sont transmis à la banque (DSP2).
    public async Task<List<ProvidedBalance>> ReadBalancesAsync(string accountUid, UserPresence? presence)
    {
        var response = await SendAsync<BalancesResponse>(
            HttpMethod.Get, "accounts/" + Uri.EscapeDataString(accountUid) + "/balances", null, presence, "accounts/{id}/balances");
        var balances = new List<ProvidedBalance>();

        foreach (var balance in response.Balances ?? new List<BalanceResource>())
        {
            balances.Add(new ProvidedBalance(
                balance.BalanceType ?? string.Empty,
                ReadCents(balance.BalanceAmount?.Amount),
                balance.BalanceAmount?.Currency ?? string.Empty));
        }

        return balances;
    }

    // Révoque la session. Une session déjà finie, ou inconnue, compte comme révoquée. Le corps de la réponse n'est pas lu.
    public async Task RevokeSessionAsync(string sessionId)
    {
        try
        {
            using var response = await SendRequestAsync(HttpMethod.Delete, "sessions/" + Uri.EscapeDataString(sessionId), null, null, "sessions/{id}");
        }
        catch (BankProviderException exception) when (exception.Failure == BankProviderFailure.SessionEnded)
        {
        }
    }

    // L'identifiant stable d'Enable Banking peut dépasser les 100 caractères de id_compte_externe : on garde son empreinte
    // SHA-256, toujours de 64 caractères, et toujours la même pour le même compte.
    public static string ComputeStableId(string identificationHash)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identificationHash)));
    }

    // Lit un montant écrit en texte, comme « -12.34 », en centimes. Renvoie null s'il n'est pas un nombre de centimes lisible.
    public static long? ReadCents(string? amount)
    {
        if (!decimal.TryParse(amount, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || value > LargestReadAmount
            || value < -LargestReadAmount)
        {
            return null;
        }

        var cents = value * 100;

        if (cents != decimal.Truncate(cents))
        {
            return null;
        }

        return (long)cents;
    }

    // Envoie une requête signée, et lit sa réponse JSON. Toute panne ou tout refus devient une BankProviderException.
    private async Task<TResponse> SendAsync<TResponse>(HttpMethod method, string path, object? body, UserPresence? presence, string routeName)
    {
        using var response = await SendRequestAsync(method, path, body, presence, routeName);

        try
        {
            var result = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions);

            if (result == null)
            {
                throw Unavailable(routeName, "Enable Banking a répondu sans contenu.");
            }

            return result;
        }
        catch (JsonException exception)
        {
            throw Unavailable(routeName, "Enable Banking a répondu un JSON illisible.", exception);
        }
    }

    // Envoie une requête signée, et renvoie sa réponse si elle est un succès. Sinon, lève une BankProviderException.
    private async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, string path, object? body, UserPresence? presence, string routeName)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenSigner.GetToken());

        if (body != null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        AddPresenceHeaders(request, presence);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request);
        }
        catch (HttpRequestException exception)
        {
            throw Unavailable(routeName, "Enable Banking ne répond pas.", exception);
        }
        catch (TaskCanceledException exception)
        {
            throw Unavailable(routeName, "Enable Banking n'a pas répondu à temps.", exception);
        }

        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                throw await ReadFailureAsync(response, routeName);
            }
        }

        return response;
    }

    // Traduit une réponse d'erreur en raison d'échec, et l'écrit dans le journal technique.
    private async Task<BankProviderException> ReadFailureAsync(HttpResponseMessage response, string routeName)
    {
        string? errorCode = null;

        try
        {
            errorCode = (await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions))?.Error;
        }
        catch (JsonException)
        {
        }
        catch (NotSupportedException)
        {
        }

        var failure = ChooseFailure(response.StatusCode, errorCode, routeName);
        logger.LogWarning(
            "Enable Banking a répondu {Statut} ({CodeErreur}) à la route {Route}.",
            (int)response.StatusCode,
            errorCode ?? "sans code",
            routeName);

        return new BankProviderException(failure, "Enable Banking a refusé la requête " + routeName + " : " + (int)response.StatusCode + ".");
    }

    // Choisit la raison d'un échec. Un refus de la création de session vient du code renvoyé par la banque :
    // faux, expiré ou déjà utilisé. Toute autre erreur inconnue compte comme une panne, à réessayer plus tard.
    private static BankProviderFailure ChooseFailure(HttpStatusCode statusCode, string? errorCode, string routeName)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            return BankProviderFailure.RateLimited;
        }

        if (errorCode != null && SessionEndedErrors.Contains(errorCode))
        {
            return BankProviderFailure.SessionEnded;
        }

        if (routeName == "sessions" && (int)statusCode >= 400 && (int)statusCode < 500)
        {
            return BankProviderFailure.AuthorizationRefused;
        }

        if (routeName == "sessions/{id}" && statusCode == HttpStatusCode.NotFound)
        {
            return BankProviderFailure.SessionEnded;
        }

        return BankProviderFailure.Unavailable;
    }

    // Ajoute l'adresse IP et le navigateur de l'utilisateur, seulement s'il est là. Sans eux, la lecture compte
    // dans les 4 lectures par jour que la banque permet sans l'utilisateur.
    private static void AddPresenceHeaders(HttpRequestMessage request, UserPresence? presence)
    {
        if (presence?.IpAddress == null)
        {
            return;
        }

        request.Headers.TryAddWithoutValidation("Psu-Ip-Address", presence.IpAddress.ToString());
        request.Headers.TryAddWithoutValidation("Psu-User-Agent", CleanHeaderValue(presence.UserAgent));
    }

    // Le navigateur est choisi par le visiteur : on n'en garde que les caractères ASCII visibles, 512 au plus.
    // Un caractère accentué ferait refuser toute la requête par le client HTTP, et la connexion passerait en erreur.
    public static string CleanHeaderValue(string? value)
    {
        var cleanValue = new StringBuilder();

        foreach (var character in value ?? string.Empty)
        {
            if (cleanValue.Length == MaximumHeaderLength)
            {
                break;
            }

            if (character >= ' ' && character <= '~')
            {
                cleanValue.Append(character);
            }
        }

        return cleanValue.ToString();
    }

    // Crée l'erreur d'une panne, et l'écrit dans le journal technique.
    private BankProviderException Unavailable(string routeName, string message, Exception? innerException = null)
    {
        logger.LogWarning("Enable Banking : {Message} Route {Route}.", message, routeName);

        return new BankProviderException(BankProviderFailure.Unavailable, message, innerException);
    }
}
