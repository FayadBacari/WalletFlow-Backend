using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WalletFlow.Application.BankConnections;
using WalletFlow.Infrastructure.BankData;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le client d'Enable Banking face à un faux serveur HTTP : les requêtes envoyées, la lecture des réponses,
// et la raison donnée à chaque erreur (GR Comptes, SS §5 et §7). Les formes des messages suivent la référence de l'API.
public sealed class EnableBankingClientTests : IDisposable
{
    private readonly RSA key = RSA.Create(2048);
    private readonly FakeTimeProvider clock = new FakeTimeProvider();
    private readonly FakeHttpHandler server = new FakeHttpHandler();
    private readonly EnableBankingTokenSigner signer;
    private readonly EnableBankingClient client;

    // Relie le client au faux serveur, avec une clé créée pour le test.
    public EnableBankingClientTests()
    {
        signer = new EnableBankingTokenSigner("application-de-test", key.ExportPkcs8PrivateKeyPem(), clock);
        client = new EnableBankingClient(
            new HttpClient(server) { BaseAddress = EnableBankingClient.ApiAddress },
            signer,
            clock,
            NullLogger<EnableBankingClient>.Instance);
    }

    // Oublie la clé du test (nom imposé par le framework).
    public void Dispose()
    {
        signer.Dispose();
        key.Dispose();
    }

    // Prépare la réponse du faux serveur : ce statut, avec ce corps JSON.
    private void Answer(HttpStatusCode statusCode, string json)
    {
        server.Respond = request => new HttpResponseMessage(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    // Lance l'appel et renvoie la raison de son échec.
    private static async Task<BankProviderFailure> CatchFailureAsync(Func<Task> call)
    {
        var exception = await Assert.ThrowsAsync<BankProviderException>(call);

        return exception.Failure;
    }

    [Fact]
    public async Task EveryRequestCarriesATokenSignedByTheApplicationKey()
    {
        Answer(HttpStatusCode.OK, """{ "aspsps": [] }""");

        await client.ListBanksAsync();

        var authorization = server.Requests.Single().Authorization;
        Assert.StartsWith("Bearer ", authorization);
        var parts = authorization!.Substring("Bearer ".Length).Split('.');
        Assert.True(key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Base64Url.DecodeFromChars(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    // Seules les banques françaises, pour les particuliers, avec une durée d'autorisation, sont gardées.
    [Fact]
    public async Task BankListKeepsOnlyUsableFrenchBanks()
    {
        Answer(HttpStatusCode.OK, """
            { "aspsps": [
                { "name": "Banque A", "country": "FR", "maximum_consent_validity": 15552000 },
                { "name": "Banque B", "country": "FR", "maximum_consent_validity": 7776000 },
                { "name": "Bank C", "country": "DE", "maximum_consent_validity": 15552000 },
                { "name": "Banque D", "country": "FR" },
                { "name": "", "country": "FR", "maximum_consent_validity": 15552000 } ] }
            """);

        var banks = await client.ListBanksAsync();

        Assert.Equal("/aspsps?country=FR&psu_type=personal&service=AIS", server.Requests.Single().PathAndQuery);
        Assert.Equal(new List<ProvidedBank> { new ProvidedBank("Banque A", TimeSpan.FromDays(180)), new ProvidedBank("Banque B", TimeSpan.FromDays(90)) }, banks);
    }

    [Fact]
    public async Task BankListIsKeptSixHours()
    {
        Answer(HttpStatusCode.OK, """{ "aspsps": [ { "name": "Banque A", "country": "FR", "maximum_consent_validity": 15552000 } ] }""");

        await client.ListBanksAsync();
        clock.Advance(TimeSpan.FromHours(6) - TimeSpan.FromSeconds(1));
        await client.ListBanksAsync();
        Assert.Single(server.Requests);

        clock.Advance(TimeSpan.FromSeconds(1));
        await client.ListBanksAsync();
        Assert.Equal(2, server.Requests.Count);
    }

    // SS §7 : la demande d'autorisation porte le state, l'adresse fixe du site et la fin choisie, pour des comptes de particuliers.
    [Fact]
    public async Task AuthorizationRequestCarriesTheStateTheReturnAddressAndTheEnd()
    {
        Answer(HttpStatusCode.OK, """{ "url": "https://banque.exemple.test/autoriser?id=1", "authorization_id": "x" }""");
        var validUntil = new DateTime(2027, 5, 11, 9, 50, 0, DateTimeKind.Utc);

        var bankAddress = await client.StartAuthorizationAsync(new BankAuthorizationRequest("Banque A", validUntil, "state-du-test", "https://walletflow.test/bank-callback"));

        var request = server.Requests.Single();
        var body = JsonDocument.Parse(request.Body!).RootElement;
        Assert.Equal("https://banque.exemple.test/autoriser?id=1", bankAddress);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/auth", request.PathAndQuery);
        Assert.Equal(new DateTimeOffset(validUntil), body.GetProperty("access").GetProperty("valid_until").GetDateTimeOffset());
        Assert.Equal("Banque A", body.GetProperty("aspsp").GetProperty("name").GetString());
        Assert.Equal("FR", body.GetProperty("aspsp").GetProperty("country").GetString());
        Assert.Equal("state-du-test", body.GetProperty("state").GetString());
        Assert.Equal("https://walletflow.test/bank-callback", body.GetProperty("redirect_url").GetString());
        Assert.Equal("personal", body.GetProperty("psu_type").GetString());
    }

    // Le site envoie l'utilisateur à cette adresse : une adresse qui n'est pas en HTTPS est refusée.
    [Theory]
    [InlineData("""{ "url": "http://banque.exemple.test/autoriser" }""")]
    [InlineData("""{ "url": "javascript:alert(1)" }""")]
    [InlineData("""{ "url": "/relative" }""")]
    [InlineData("""{ }""")]
    public async Task BankAddressThatIsNotHttpsIsRefused(string json)
    {
        Answer(HttpStatusCode.OK, json);

        var failure = await CatchFailureAsync(() => client.StartAuthorizationAsync(
            new BankAuthorizationRequest("Banque A", DateTime.UtcNow, "state", "https://walletflow.test/bank-callback")));

        Assert.Equal(BankProviderFailure.Unavailable, failure);
    }

    // SS §7 : le nom du titulaire n'est jamais lu, et l'identifiant stable est gardé par son empreinte de 64 caractères.
    [Fact]
    public async Task SessionGivesAccountsWithTheirStableIdAndNeverTheHolderName()
    {
        var longHash = new string('h', 150);
        Answer(HttpStatusCode.OK, $$"""
            { "session_id": "session-1", "access": { "valid_until": "2027-05-11T09:50:00+00:00" },
              "accounts": [
                { "uid": "uid-1", "identification_hash": "{{longHash}}", "currency": "EUR", "name": "Jean Dupont", "details": "Compte courant" },
                { "uid": "uid-2", "identification_hash": "empreinte-2", "currency": "USD", "name": "Jean Dupont", "product": "Compte en dollars" },
                { "uid": null, "identification_hash": "empreinte-3", "currency": "EUR" },
                { "uid": "uid-4", "currency": "EUR" } ] }
            """);

        var session = await client.CreateSessionAsync("code-du-test");

        Assert.Equal("/sessions", server.Requests.Single().PathAndQuery);
        Assert.Equal("code-du-test", JsonDocument.Parse(server.Requests.Single().Body!).RootElement.GetProperty("code").GetString());
        Assert.Equal("session-1", session.SessionId);
        Assert.Equal(new DateTime(2027, 5, 11, 9, 50, 0, DateTimeKind.Utc), session.ValidUntil);
        Assert.Equal(2, session.Accounts.Count);
        Assert.Equal(new ProvidedAccount("uid-1", EnableBankingClient.ComputeStableId(longHash), "Compte courant", "EUR"), session.Accounts[0]);
        Assert.Equal(new ProvidedAccount("uid-2", EnableBankingClient.ComputeStableId("empreinte-2"), "Compte en dollars", "USD"), session.Accounts[1]);
        Assert.Equal(64, session.Accounts[0].StableId.Length);
        Assert.DoesNotContain(session.Accounts, account => account.Name == "Jean Dupont");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{ "error": "WRONG_AUTHORIZATION_CODE" }""", BankProviderFailure.AuthorizationRefused)]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{ }""", BankProviderFailure.AuthorizationRefused)]
    [InlineData(HttpStatusCode.Unauthorized, """{ "error": "EXPIRED_AUTHORIZATION_CODE" }""", BankProviderFailure.AuthorizationRefused)]
    [InlineData(HttpStatusCode.TooManyRequests, """{ }""", BankProviderFailure.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, """pas du json""", BankProviderFailure.Unavailable)]
    [InlineData(HttpStatusCode.OK, """{ "accounts": [] }""", BankProviderFailure.Unavailable)]
    public async Task SessionCreationFailuresHaveTheirReason(HttpStatusCode statusCode, string json, BankProviderFailure expectedFailure)
    {
        Answer(statusCode, json);

        Assert.Equal(expectedFailure, await CatchFailureAsync(() => client.CreateSessionAsync("code")));
    }

    [Fact]
    public async Task AuthorizedSessionGivesItsAccounts()
    {
        Answer(HttpStatusCode.OK, """
            { "status": "AUTHORIZED", "accounts_data": [
                { "uid": "uid-1", "identification_hash": "empreinte-1" },
                { "uid": "uid-2" } ] }
            """);

        var accounts = await client.ReadSessionAccountsAsync("session-1");

        Assert.Equal("/sessions/session-1", server.Requests.Single().PathAndQuery);
        Assert.Equal(new List<SessionAccount> { new SessionAccount("uid-1", EnableBankingClient.ComputeStableId("empreinte-1")) }, accounts);
    }

    // Une session qui n'est plus autorisée, ou que le prestataire ne connaît plus, demande une nouvelle autorisation.
    [Theory]
    [InlineData(HttpStatusCode.OK, """{ "status": "EXPIRED", "accounts_data": [] }""")]
    [InlineData(HttpStatusCode.OK, """{ "status": "REVOKED", "accounts_data": [] }""")]
    [InlineData(HttpStatusCode.Unauthorized, """{ "error": "EXPIRED_SESSION" }""")]
    [InlineData(HttpStatusCode.BadRequest, """{ "error": "REVOKED_SESSION" }""")]
    [InlineData(HttpStatusCode.NotFound, """{ }""")]
    public async Task EndedSessionIsReportedAsEnded(HttpStatusCode statusCode, string json)
    {
        Answer(statusCode, json);

        Assert.Equal(BankProviderFailure.SessionEnded, await CatchFailureAsync(() => client.ReadSessionAccountsAsync("session-1")));
    }

    // Un identifiant reçu ne peut pas changer de route : il est échappé dans l'adresse.
    [Fact]
    public async Task IdentifiersCannotChangeTheRoute()
    {
        Answer(HttpStatusCode.OK, """{ "balances": [] }""");

        await client.ReadBalancesAsync("../aspsps?country=DE", null);

        Assert.Equal("/accounts/..%2Faspsps%3Fcountry%3DDE/balances", server.Requests.Single().PathAndQuery);
    }

    [Fact]
    public async Task BalancesAreReadInCentsWithTheirTypeAndCurrency()
    {
        Answer(HttpStatusCode.OK, """
            { "balances": [
                { "balance_amount": { "currency": "EUR", "amount": "1234.56" }, "balance_type": "CLBD" },
                { "balance_amount": { "currency": "EUR", "amount": "illisible" }, "balance_type": "ITAV" } ] }
            """);

        var balances = await client.ReadBalancesAsync("uid-1", null);

        Assert.Equal(new ProvidedBalance("CLBD", 123456, "EUR"), balances[0]);
        Assert.Equal(new ProvidedBalance("ITAV", null, "EUR"), balances[1]);
    }

    // SS §5 : un montant est un nombre entier de centimes. Plus de deux décimales, un exposant ou un texte ne se lisent pas.
    [Theory]
    [InlineData("123.45", 12345L)]
    [InlineData("-0.5", -50L)]
    [InlineData("10", 1000L)]
    [InlineData("0.00", 0L)]
    [InlineData("1.234", null)]
    [InlineData("1e5", null)]
    [InlineData("12,50", null)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("99999999999999999999999999", null)]
    public void AmountIsReadInWholeCents(string? amount, long? expectedCents)
    {
        Assert.Equal(expectedCents, EnableBankingClient.ReadCents(amount));
    }

    // DSP2 : l'adresse IP et le navigateur ne partent que si l'utilisateur est là. Le navigateur est nettoyé avant.
    [Fact]
    public async Task UserHeadersAreSentOnlyWhenTheUserIsThere()
    {
        Answer(HttpStatusCode.OK, """{ "balances": [] }""");
        var userAgent = "Mozilla" + (char)0x00E9 + "\r\n<script>";

        await client.ReadBalancesAsync("uid-1", new UserPresence(IPAddress.Parse("203.0.113.7"), userAgent));
        await client.ReadBalancesAsync("uid-1", null);

        Assert.Equal("203.0.113.7", server.Requests[0].Headers["Psu-Ip-Address"]);
        Assert.Equal("Mozilla<script>", server.Requests[0].Headers["Psu-User-Agent"]);
        Assert.False(server.Requests[1].Headers.ContainsKey("Psu-Ip-Address"));
        Assert.False(server.Requests[1].Headers.ContainsKey("Psu-User-Agent"));
    }

    [Fact]
    public void HeaderValueKeepsOnlyVisibleAsciiAndAtMost512Characters()
    {
        Assert.Equal(512, EnableBankingClient.CleanHeaderValue(new string('a', 600)).Length);
        Assert.Equal(string.Empty, EnableBankingClient.CleanHeaderValue(null));
        Assert.Equal("a b", EnableBankingClient.CleanHeaderValue("a" + (char)0x0007 + " b" + (char)0x2028));
    }

    // Une session déjà finie, ou inconnue, compte comme révoquée. Le corps de la réponse n'est pas lu.
    [Theory]
    [InlineData(HttpStatusCode.OK, "")]
    [InlineData(HttpStatusCode.OK, """{ "message": "OK" }""")]
    [InlineData(HttpStatusCode.NotFound, """{ "error": "SESSION_DOES_NOT_EXIST" }""")]
    [InlineData(HttpStatusCode.Unauthorized, """{ "error": "EXPIRED_SESSION" }""")]
    public async Task RevocationSucceedsWhenTheSessionIsAlreadyGone(HttpStatusCode statusCode, string body)
    {
        Answer(statusCode, body);

        await client.RevokeSessionAsync("session-1");

        Assert.Equal(HttpMethod.Delete, server.Requests.Single().Method);
        Assert.Equal("/sessions/session-1", server.Requests.Single().PathAndQuery);
    }

    [Fact]
    public async Task RevocationThatTheProviderDoesNotConfirmFails()
    {
        Answer(HttpStatusCode.InternalServerError, """{ "error": "ASPSP_ERROR" }""");

        Assert.Equal(BankProviderFailure.Unavailable, await CatchFailureAsync(() => client.RevokeSessionAsync("session-1")));
    }

    // Un réseau en panne, une attente trop longue ou une réponse illisible : le prestataire ne répond pas.
    [Fact]
    public async Task NetworkFailuresAreUnavailability()
    {
        server.Failure = new HttpRequestException("réseau coupé");
        Assert.Equal(BankProviderFailure.Unavailable, await CatchFailureAsync(() => client.ReadBalancesAsync("uid-1", null)));

        server.Failure = new TaskCanceledException("trop long");
        Assert.Equal(BankProviderFailure.Unavailable, await CatchFailureAsync(() => client.ReadBalancesAsync("uid-1", null)));

        server.Failure = null;
        Answer(HttpStatusCode.OK, "{");
        Assert.Equal(BankProviderFailure.Unavailable, await CatchFailureAsync(() => client.ReadBalancesAsync("uid-1", null)));
    }

    // Prépare les pages d'opérations du faux serveur : chaque requête reçoit la page suivante, puis la dernière à nouveau.
    private void AnswerPages(params string[] pages)
    {
        var pageNumber = 0;

        server.Respond = request =>
        {
            var json = pages[Math.Min(pageNumber, pages.Length - 1)];
            pageNumber = pageNumber + 1;

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        };
    }

    // Les opérations sont demandées depuis le jour choisi, avec la stratégie « longest », puis page après page avec la clé reçue,
    // en gardant les mêmes paramètres, jusqu'à ce qu'aucune clé ne vienne. Une page vide avec une clé n'arrête pas la lecture.
    [Fact]
    public async Task OperationsAreReadPageAfterPageWithTheSameParameters()
    {
        AnswerPages(
            """{ "transactions": [ { "status": "BOOK", "credit_debit_indicator": "DBIT", "transaction_amount": { "currency": "EUR", "amount": "12.50" }, "booking_date": "2026-11-02", "remittance_information": [ "CB CARREFOUR" ] } ], "continuation_key": "cle/1+2" }""",
            """{ "transactions": [], "continuation_key": "cle-2" }""",
            """{ "transactions": [ { "status": "BOOK", "credit_debit_indicator": "CRDT", "transaction_amount": { "currency": "EUR", "amount": "100.00" }, "booking_date": "2026-11-03", "remittance_information": [ "VIR SALAIRE" ] } ], "continuation_key": null }""");

        var transactions = await client.ReadTransactionsAsync("../aspsps", new DateOnly(2025, 11, 12), null);

        Assert.Equal(new List<string>
        {
            "/accounts/..%2Faspsps/transactions?date_from=2025-11-12&strategy=longest",
            "/accounts/..%2Faspsps/transactions?date_from=2025-11-12&strategy=longest&continuation_key=cle%2F1%2B2",
            "/accounts/..%2Faspsps/transactions?date_from=2025-11-12&strategy=longest&continuation_key=cle-2"
        }, server.Requests.Select(request => request.PathAndQuery).ToList());
        Assert.All(server.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        Assert.Equal(new List<long?> { -1250, 10_000 }, transactions.Select(transaction => transaction.AmountInCents).ToList());
    }

    // Chaque opération est lue sans rien croire : comptabilisée ou non, le sens par l'indicateur et jamais par le signe,
    // la date comptable ou à défaut la date de valeur, la devise et la référence de la banque.
    [Fact]
    public async Task OperationFieldsAreReadSafely()
    {
        Answer(HttpStatusCode.OK, """
            { "transactions": [
                { "entry_reference": "REF-1", "status": "BOOK", "credit_debit_indicator": "DBIT", "transaction_amount": { "currency": "EUR", "amount": "-12.34" }, "booking_date": "2026-11-02", "value_date": "2026-11-01" },
                { "status": "PDNG", "credit_debit_indicator": "CRDT", "transaction_amount": { "currency": "USD", "amount": "5" }, "value_date": "2026-11-03" },
                { "status": "BOOK", "credit_debit_indicator": "AUTRE", "transaction_amount": { "currency": "EUR", "amount": "1.00" }, "booking_date": "pas une date" },
                { "status": "BOOK", "credit_debit_indicator": "DBIT", "transaction_amount": { "currency": "EUR", "amount": "1.005" } },
                { "status": "BOOK", "credit_debit_indicator": "CRDT", "transaction_amount": { "currency": "EUR", "amount": "-3.00" }, "booking_date": "2026-11-04" }
            ] }
            """);

        var transactions = await client.ReadTransactionsAsync("uid-1", new DateOnly(2026, 1, 1), null);

        Assert.Equal(new ProvidedTransaction("REF-1", true, new DateOnly(2026, 11, 2), -1234, "EUR", string.Empty), transactions[0]);
        Assert.Equal(new ProvidedTransaction(null, false, new DateOnly(2026, 11, 3), 500, "USD", string.Empty), transactions[1]);
        Assert.Null(transactions[2].AmountInCents);
        Assert.Null(transactions[2].Date);
        Assert.Null(transactions[3].AmountInCents);
        Assert.Equal(300, transactions[4].AmountInCents);
    }

    // Le libellé : les lignes de texte de la banque, sinon le nom de l'autre partie, sinon le type d'opération.
    // Les comptes de l'autre partie ne sont jamais lus (DD §13).
    [Fact]
    public async Task LabelComesFromTheTextThenTheOtherPartyThenTheType()
    {
        Answer(HttpStatusCode.OK, """
            { "transactions": [
                { "status": "BOOK", "credit_debit_indicator": "DBIT", "remittance_information": [ "CB CARREFOUR", " ", null, "PARIS 12" ], "creditor": { "name": "CARREFOUR SAS" } },
                { "status": "BOOK", "credit_debit_indicator": "DBIT", "remittance_information": [], "creditor": { "name": "EDF" }, "debtor": { "name": "MOI" }, "creditor_account": { "iban": "FR00COMPTEDETEST" } },
                { "status": "BOOK", "credit_debit_indicator": "CRDT", "creditor": { "name": "MOI" }, "debtor": { "name": "EMPLOYEUR SAS" } },
                { "status": "BOOK", "credit_debit_indicator": "DBIT", "bank_transaction_code": { "description": "Frais bancaires" } }
            ] }
            """);

        var transactions = await client.ReadTransactionsAsync("uid-1", new DateOnly(2026, 1, 1), null);

        Assert.Equal(
            new List<string> { "CB CARREFOUR PARIS 12", "EDF", "EMPLOYEUR SAS", "Frais bancaires" },
            transactions.Select(transaction => transaction.Label).ToList());
        Assert.DoesNotContain(transactions, transaction => transaction.Label.Contains("FR00"));
    }

    // DSP2 : l'adresse IP et le navigateur partent avec chaque page quand l'utilisateur est là, jamais sinon.
    [Fact]
    public async Task UserHeadersGoWithEveryPageOnlyWhenTheUserIsThere()
    {
        AnswerPages("""{ "transactions": [], "continuation_key": "cle-1" }""", """{ "transactions": [] }""");
        await client.ReadTransactionsAsync("uid-1", new DateOnly(2026, 1, 1), new UserPresence(IPAddress.Parse("203.0.113.7"), "Navigateur"));

        Assert.Equal(2, server.Requests.Count);
        Assert.All(server.Requests, request => Assert.Equal("203.0.113.7", request.Headers["Psu-Ip-Address"]));

        server.Requests.Clear();
        AnswerPages("""{ "transactions": [] }""");
        await client.ReadTransactionsAsync("uid-1", new DateOnly(2026, 1, 1), null);

        Assert.False(server.Requests.Single().Headers.ContainsKey("Psu-Ip-Address"));
    }

    // Un prestataire qui donnerait des pages sans fin ne bloque jamais une mise à jour : la lecture s'arrête comme une panne.
    [Fact]
    public async Task EndlessPagesStopAsUnavailability()
    {
        Answer(HttpStatusCode.OK, """{ "transactions": [], "continuation_key": "encore" }""");

        var failure = await CatchFailureAsync(() => client.ReadTransactionsAsync("uid-1", new DateOnly(2026, 1, 1), null));

        Assert.Equal(BankProviderFailure.Unavailable, failure);
        Assert.Equal(EnableBankingClient.MaximumTransactionPages, server.Requests.Count);
    }

    // Les erreurs d'une lecture d'opérations ont leur raison, comme celles des soldes.
    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, """{ "error": "ASPSP_RATE_LIMIT_EXCEEDED" }""", BankProviderFailure.RateLimited)]
    [InlineData(HttpStatusCode.Unauthorized, """{ "error": "EXPIRED_SESSION" }""", BankProviderFailure.SessionEnded)]
    [InlineData(HttpStatusCode.BadRequest, """{ "error": "WRONG_TRANSACTIONS_PERIOD" }""", BankProviderFailure.Unavailable)]
    [InlineData(HttpStatusCode.OK, """{ "transactions": [ { "remittance_information": "pas une liste" } ] }""", BankProviderFailure.Unavailable)]
    public async Task OperationReadingFailuresHaveTheirReason(HttpStatusCode statusCode, string json, BankProviderFailure expectedFailure)
    {
        Answer(statusCode, json);

        Assert.Equal(expectedFailure, await CatchFailureAsync(() => client.ReadTransactionsAsync("uid-1", new DateOnly(2026, 1, 1), null)));
    }

    // Un faux serveur HTTP : chaque requête reçoit la réponse préparée par le test, et est gardée pour être vérifiée.
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = request => new HttpResponseMessage(HttpStatusCode.OK);
        public Exception? Failure { get; set; } = null;

        // Garde la requête, puis répond ou échoue (nom imposé par le framework).
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var headers = request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value));
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(), headers, body));

            if (Failure != null)
            {
                throw Failure;
            }

            return Respond(request);
        }
    }

    // Une requête reçue par le faux serveur.
    private sealed record RecordedRequest(HttpMethod Method, string PathAndQuery, string? Authorization, Dictionary<string, string> Headers, string? Body);
}
