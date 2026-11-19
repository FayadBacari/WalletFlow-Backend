using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using WalletFlow.Infrastructure.Security;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie la recherche des mots de passe des fuites connues avec Pwned Passwords (SS §2), sur un faux serveur HTTP : aucun
// test n'interroge le vrai service.
public sealed class PwnedPasswordsCheckerTests : IDisposable
{
    // L'empreinte SHA-1 de ce mot de passe : ses 5 premiers caractères, puis la fin que le service renvoie s'il le connaît.
    private const string Password = "Mot-De-Passe-Divulgue-7";
    private const string PasswordPrefix = "20365";
    private const string PasswordSuffix = "039CB538F3901755162C22BDF50A7DE8BA7";

    private readonly FakeHttpHandler server = new FakeHttpHandler();
    private readonly MemoryLoggerProvider loggerProvider = new MemoryLoggerProvider();
    private readonly PwnedPasswordsChecker checker;

    // Branche le vrai client sur le faux serveur.
    public PwnedPasswordsCheckerTests()
    {
        var loggerFactory = new LoggerFactory(new ILoggerProvider[] { loggerProvider });
        checker = new PwnedPasswordsChecker(PwnedPasswordsChecker.CreateHttpClient(server), new Logger<PwnedPasswordsChecker>(loggerFactory));
    }

    // Prépare la réponse du faux serveur.
    private void Answer(HttpStatusCode statusCode, string body)
    {
        server.Respond = () => new HttpResponseMessage(statusCode) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
    }

    // Seuls les 5 premiers caractères de l'empreinte partent, avec l'en-tête qui fait ajouter de fausses lignes : ni le mot de
    // passe ni la fin de son empreinte ne quittent le serveur (k-anonymat).
    [Fact]
    public async Task OnlyTheFirstFiveCharactersOfTheHashLeaveTheServer()
    {
        Answer(HttpStatusCode.OK, "");

        await checker.IsBreachedAsync(Password);

        var request = Assert.Single(server.Requests);
        Assert.Equal("https://api.pwnedpasswords.com/range/" + PasswordPrefix, request.Address);
        Assert.Equal("true", request.Padding);
        Assert.DoesNotContain(PasswordSuffix, request.Address, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasswordWhoseSuffixIsListedIsBreached()
    {
        Answer(HttpStatusCode.OK, "0018A45C4D1DEF81644B54AB7F969B88D65:1\r\n" + PasswordSuffix + ":9545824\r\n00D4F6E8FA6EECAD2A3AA415EEC418D38EC:2");

        Assert.True(await checker.IsBreachedAsync(Password));
    }

    // Une fin écrite en minuscules désigne la même empreinte.
    [Fact]
    public async Task SuffixIsComparedWithoutCase()
    {
        Answer(HttpStatusCode.OK, PasswordSuffix.ToLowerInvariant() + ":3");

        Assert.True(await checker.IsBreachedAsync(Password));
    }

    // Les fausses lignes ajoutées par le service comptent 0 fois : elles ne désignent aucun mot de passe divulgué. Une fin
    // absente, ou une ligne illisible, non plus.
    [Theory]
    [InlineData(PasswordSuffix + ":0")]
    [InlineData("0018A45C4D1DEF81644B54AB7F969B88D65:12")]
    [InlineData(PasswordSuffix)]
    [InlineData(PasswordSuffix + ":beaucoup")]
    [InlineData(PasswordSuffix + ":-4")]
    [InlineData("")]
    public async Task PaddingAbsentOrUnreadableLineIsNotABreach(string body)
    {
        Answer(HttpStatusCode.OK, body);

        Assert.False(await checker.IsBreachedAsync(Password));
    }

    // Une panne du service n'empêche personne de choisir son mot de passe : il passe, et la panne va au journal technique, sans
    // rien du mot de passe.
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task FailingServiceLetsThePasswordThroughAndIsLogged(HttpStatusCode statusCode)
    {
        Answer(statusCode, PasswordSuffix + ":12");

        Assert.False(await checker.IsBreachedAsync(Password));
        AssertWarningWithoutPassword();
    }

    [Fact]
    public async Task UnreachableServiceLetsThePasswordThroughAndIsLogged()
    {
        server.Failure = new HttpRequestException("réseau coupé");

        Assert.False(await checker.IsBreachedAsync(Password));
        AssertWarningWithoutPassword();
    }

    // Le service ne répond pas dans les 3 secondes : le mot de passe passe.
    [Fact]
    public async Task SilentServiceLetsThePasswordThroughAndIsLogged()
    {
        server.Failure = new TaskCanceledException("trop long");

        Assert.False(await checker.IsBreachedAsync(Password));
        AssertWarningWithoutPassword();
        Assert.Equal(TimeSpan.FromSeconds(3), PwnedPasswordsChecker.MaximumWait);
    }

    // Une réponse démesurée n'est pas lue en entier : le mot de passe passe, et rien ne remplit la mémoire.
    [Fact]
    public async Task OversizedAnswerIsNotRead()
    {
        Answer(HttpStatusCode.OK, PasswordSuffix + ":5\r\n" + new string('A', PwnedPasswordsChecker.MaximumResponseBytes));

        Assert.False(await checker.IsBreachedAsync(Password));
        AssertWarningWithoutPassword();
    }

    // Une requête abandonnée par son client n'est pas une panne du service : l'annulation remonte, rien n'est accepté à sa place.
    [Fact]
    public async Task AbandonedRequestIsNotTakenForASilentService()
    {
        server.WaitsForever = true;
        using var abandon = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checker.IsBreachedAsync(Password, abandon.Token));
    }

    // Vérifie que la panne est notée au journal technique, sans le mot de passe ni son empreinte.
    private void AssertWarningWithoutPassword()
    {
        var message = Assert.Single(loggerProvider.Messages);
        Assert.StartsWith("Warning", message);
        Assert.DoesNotContain(Password, message);
        Assert.DoesNotContain(PasswordPrefix, message);
    }

    // Un faux serveur HTTP : chaque requête reçoit la réponse préparée par le test, et est gardée pour être vérifiée.
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        public List<(string Address, string? Padding)> Requests { get; } = new List<(string Address, string? Padding)>();
        public Func<HttpResponseMessage> Respond { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK);
        public Exception? Failure { get; set; } = null;
        public bool WaitsForever { get; set; } = false;

        // Garde la requête, puis répond ou échoue (nom imposé par le framework).
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var padding = request.Headers.TryGetValues("Add-Padding", out var values) ? string.Join(",", values) : null;
            Requests.Add((request.RequestUri!.ToString(), padding));

            if (Failure != null)
            {
                throw Failure;
            }

            if (WaitsForever)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return Respond();
        }
    }

    // Libère le journal technique et le faux serveur à la fin du test.
    public void Dispose()
    {
        loggerProvider.Dispose();
        server.Dispose();
    }
}
