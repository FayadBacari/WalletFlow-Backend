using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using MimeKit;
using WalletFlow.Application.Emails;
using WalletFlow.Infrastructure.Emails;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie l'envoi des e-mails par SMTP (SS §7 et §9) contre un faux serveur qui parle le vrai protocole : chiffrement exigé avant
// le mot de passe, certificat vérifié, pannes rendues réessayables, journal technique sans adresse, et aucun e-mail en double.
public sealed class SmtpEmailSenderTests : IDisposable
{
    private const string Username = "compte-envoi";
    private const string Password = "Exemple-De-Mot-De-Passe-7";
    private const string SenderAddress = "noreply@walletflow.test";
    private const string Recipient = "destinataire@exemple.test";

    private readonly FakeSmtpServer server = new FakeSmtpServer();
    private readonly MemoryLoggerProvider loggerProvider = new MemoryLoggerProvider();
    private readonly LoggerFactory loggerFactory;

    // Prépare le journal technique que lit chaque test.
    public SmtpEmailSenderTests()
    {
        loggerFactory = new LoggerFactory(new ILoggerProvider[] { loggerProvider });
    }

    // Arrête le faux serveur et ferme le journal à la fin du test.
    public void Dispose()
    {
        server.Dispose();
        loggerFactory.Dispose();
        loggerProvider.Dispose();
    }

    // Crée l'envoi vers le faux serveur, avec le client de production, qui ne fait confiance qu'au certificat de ce serveur et
    // attend moins longtemps. Sans confiance donnée, le client vérifie le certificat comme en production.
    private SmtpEmailSender CreateSender(int? port = null, bool trustsTheServer = true)
    {
        var settings = new SmtpServerSettings("127.0.0.1", port ?? server.Port, Username, Password, SenderAddress, "WalletFlow");

        return new SmtpEmailSender(
            settings,
            () =>
            {
                var client = SmtpEmailSender.CreateClient();
                client.Timeout = 2000;
                if (trustsTheServer)
                {
                    client.ServerCertificateValidationCallback = (sender, certificate, chain, errors) =>
                        certificate != null && certificate.GetCertHashString() == server.Certificate.GetCertHashString();
                }

                return client;
            },
            loggerFactory.CreateLogger<SmtpEmailSender>());
    }

    // Donne les messages du journal technique écrits à ce niveau.
    private List<string> ReadMessages(LogLevel level)
    {
        return loggerProvider.Messages.Where(message => message.StartsWith(level + " : ", StringComparison.Ordinal)).ToList();
    }

    // Vrai si le faux serveur a reçu une commande AUTH, chiffrée ou non.
    private bool ReceivedALogin()
    {
        return server.Commands.Any(command => command.StartsWith("AUTH", StringComparison.Ordinal));
    }

    // L'e-mail part après STARTTLS, avec le compte d'envoi : l'enveloppe et les en-têtes portent l'expéditeur des réglages et le
    // destinataire, l'objet et le texte arrivent entiers, accents compris.
    [Fact]
    public async Task EmailLeavesEncryptedWithTheSendingAccount()
    {
        await CreateSender().SendAsync(new EmailMessage(Recipient, "Ton compte WalletFlow est prêt", "Bonjour,\nTon compte est prêt.\n.Ligne qui commence par un point."));

        var received = Assert.Single(server.ReceivedEmails);
        Assert.Equal((Username, Password, true), (received.LoginUser, received.LoginPassword, received.WasEncryptedAtLogin));
        Assert.Equal(SenderAddress, received.MailFrom);
        Assert.Equal(new List<string> { Recipient }, received.Recipients);
        var message = MimeMessage.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(received.Data)));
        Assert.Equal("WalletFlow", message.From.Mailboxes.Single().Name);
        Assert.Equal(SenderAddress, message.From.Mailboxes.Single().Address);
        Assert.Equal(Recipient, message.To.Mailboxes.Single().Address);
        Assert.Equal("Ton compte WalletFlow est prêt", message.Subject);
        var body = Assert.IsType<string>(message.TextBody);
        Assert.Equal("Bonjour,\nTon compte est prêt.\n.Ligne qui commence par un point.", body.ReplaceLineEndings("\n").TrimEnd('\n'));
        Assert.Contains("STARTTLS en clair", server.Commands);
        Assert.DoesNotContain("AUTH en clair", server.Commands);
    }

    // SS §7 : un serveur qui ne propose pas STARTTLS est refusé avant que le mot de passe parte. L'essai est une panne
    // réessayable, et rien n'est envoyé.
    [Fact]
    public async Task ServerWithoutStartTlsIsRefusedBeforeThePasswordLeaves()
    {
        server.OffersStartTls = false;

        await Assert.ThrowsAsync<EmailCouldNotBeSentException>(() => CreateSender().SendAsync(new EmailMessage(Recipient, "Objet", "Texte")));

        Assert.False(ReceivedALogin());
        Assert.Empty(server.ReceivedEmails);
    }

    // Un serveur dont le certificat n'est pas reconnu par le système est refusé, comme le serait un intermédiaire qui se ferait
    // passer pour le prestataire : le mot de passe ne part pas.
    [Fact]
    public async Task UnknownCertificateIsRefusedBeforeThePasswordLeaves()
    {
        await Assert.ThrowsAsync<EmailCouldNotBeSentException>(() =>
            CreateSender(trustsTheServer: false).SendAsync(new EmailMessage(Recipient, "Objet", "Texte")));

        Assert.False(ReceivedALogin());
        Assert.Empty(server.ReceivedEmails);
    }

    // Un compte d'envoi refusé, ou un destinataire refusé, est une panne réessayable. Le journal technique nomme l'erreur et son
    // code, jamais l'adresse du destinataire, même quand le serveur la cite (SS §7).
    [Fact]
    public async Task RefusalsAreRetryableAndNeverWriteTheAddress()
    {
        server.RejectsLogin = true;
        await Assert.ThrowsAsync<EmailCouldNotBeSentException>(() => CreateSender().SendAsync(new EmailMessage(Recipient, "Objet", "Texte")));

        server.RejectsLogin = false;
        server.RecipientRefusal = "550 5.1.1 <" + Recipient + "> : boîte inconnue";
        await Assert.ThrowsAsync<EmailCouldNotBeSentException>(() => CreateSender().SendAsync(new EmailMessage(Recipient, "Objet", "Texte")));

        Assert.Empty(server.ReceivedEmails);
        var warnings = ReadMessages(LogLevel.Warning);
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, warning => warning.Contains("AuthenticationException", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Contains("SmtpCommandException RecipientNotAccepted 550", StringComparison.Ordinal));
        Assert.DoesNotContain(loggerProvider.Messages, message => message.Contains(Recipient, StringComparison.Ordinal));
        Assert.DoesNotContain(loggerProvider.Messages, message => message.Contains(Password, StringComparison.Ordinal));
    }

    // Un serveur muet fait échouer l'essai une fois le délai passé, au lieu de bloquer la file.
    [Fact]
    public async Task SilentServerFailsAfterTheTimeout()
    {
        server.StaysSilent = true;

        await Assert.ThrowsAsync<EmailCouldNotBeSentException>(() => CreateSender().SendAsync(new EmailMessage(Recipient, "Objet", "Texte")));
    }

    // Personne n'écoute à cette adresse : l'essai est une panne réessayable.
    [Fact]
    public async Task NoServerIsARetryableFailure()
    {
        var closedListener = new TcpListener(IPAddress.Loopback, 0);
        closedListener.Start();
        var closedPort = ((IPEndPoint)closedListener.LocalEndpoint).Port;
        closedListener.Stop();

        await Assert.ThrowsAsync<EmailCouldNotBeSentException>(() =>
            CreateSender(port: closedPort).SendAsync(new EmailMessage(Recipient, "Objet", "Texte")));
    }

    // Une fois l'e-mail accepté, une connexion coupée avant la fin ne fait pas échouer l'envoi : sinon, la file le renverrait
    // en double.
    [Fact]
    public async Task ConnectionCutAfterAcceptanceStillCountsAsSent()
    {
        server.ClosesAfterAcceptance = true;

        await CreateSender().SendAsync(new EmailMessage(Recipient, "Objet", "Texte"));

        Assert.Single(server.ReceivedEmails);
        Assert.Empty(ReadMessages(LogLevel.Warning));
    }

    // L'API s'arrête pendant que la connexion se termine, l'e-mail déjà accepté : l'envoi compte quand même comme fait, sinon
    // la file le renverrait en double au prochain démarrage. Un arrêt avant la réponse du serveur, lui, remonte : l'e-mail
    // repartira, car mieux vaut un e-mail en double qu'un e-mail perdu (SS §9).
    [Fact]
    public async Task ShutdownWhileClosingStillCountsAsSent()
    {
        using var shutdown = new CancellationTokenSource();
        server.OnQuit = shutdown.Cancel;

        await CreateSender().SendAsync(new EmailMessage(Recipient, "Objet", "Texte"), shutdown.Token);

        Assert.Single(server.ReceivedEmails);
        Assert.True(shutdown.IsCancellationRequested);
    }

    // L'arrêt de l'API n'est pas une panne de l'envoi : l'annulation remonte telle quelle, sans rien noter au journal.
    [Fact]
    public async Task ShutdownIsNotASendingFailure()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateSender().SendAsync(new EmailMessage(Recipient, "Objet", "Texte"), new CancellationToken(true)));

        Assert.Empty(server.ReceivedEmails);
        Assert.Empty(ReadMessages(LogLevel.Warning));
    }

    // Le client de production exige TLS 1.2 au moins, laisse le système vérifier le certificat du serveur, et n'attend pas plus
    // de 30 secondes une réponse.
    [Fact]
    public void ProductionClientRequiresTlsAndChecksCertificates()
    {
        using var client = SmtpEmailSender.CreateClient();

        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, client.SslProtocols);
        Assert.Null(client.ServerCertificateValidationCallback);
        Assert.Equal(30_000, client.Timeout);
    }
}
