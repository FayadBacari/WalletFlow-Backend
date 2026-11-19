using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using WalletFlow.Tests.Api;

namespace WalletFlow.Tests.Fakes;

// Un faux serveur SMTP sur la machine des tests : il parle le vrai protocole, STARTTLS compris avec un certificat jetable, et
// note ce qu'il reçoit. Ses réglages simulent un serveur sans STARTTLS, un compte refusé, un destinataire refusé, une connexion
// coupée juste après l'acceptation, ou un serveur muet.
public sealed class FakeSmtpServer : IDisposable
{
    private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new CancellationTokenSource();
    private readonly Task acceptingTask;

    // Démarre le serveur sur un port libre.
    public FakeSmtpServer()
    {
        Certificate = ThrowawayHttpsCertificate.Create();
        listener.Start();
        acceptingTask = AcceptConnectionsAsync();
    }

    public X509Certificate2 Certificate { get; }
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    // Les réglages du faux serveur.
    public bool OffersStartTls { get; set; } = true;
    public bool RejectsLogin { get; set; } = false;
    public string? RecipientRefusal { get; set; } = null;
    public bool ClosesAfterAcceptance { get; set; } = false;
    public bool StaysSilent { get; set; } = false;

    // Agit quand le client termine la connexion, avant la réponse du serveur : un test y arrête l'API, par exemple.
    public Action? OnQuit { get; set; } = null;

    // Ce que le serveur a reçu : chaque commande, avec « chiffré » ou « en clair », et chaque e-mail accepté.
    public ConcurrentQueue<string> Commands { get; } = new ConcurrentQueue<string>();
    public ConcurrentQueue<ReceivedEmail> ReceivedEmails { get; } = new ConcurrentQueue<ReceivedEmail>();

    // Arrête le serveur et ferme ses connexions.
    public void Dispose()
    {
        stopping.Cancel();
        listener.Stop();
        try
        {
            acceptingTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Le serveur s'arrête : l'attente des connexions finit sur une annulation.
        }

        Certificate.Dispose();
        stopping.Dispose();
    }

    // Accepte chaque connexion et la sert à part, jusqu'à l'arrêt du serveur.
    private async Task AcceptConnectionsAsync()
    {
        while (!stopping.IsCancellationRequested)
        {
            TcpClient connection;
            try
            {
                connection = await listener.AcceptTcpClientAsync(stopping.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(connection));
        }
    }

    // Sert une connexion. Une connexion coupée par le client finit simplement la conversation.
    private async Task ServeAsync(TcpClient connection)
    {
        using (connection)
        {
            try
            {
                await ConverseAsync(connection.GetStream());
            }
            catch (IOException)
            {
            }
            catch (AuthenticationException)
            {
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    // Mène la conversation SMTP, commande après commande.
    private async Task ConverseAsync(Stream networkStream)
    {
        if (StaysSilent)
        {
            await Task.Delay(Timeout.Infinite, stopping.Token);
        }

        Stream stream = networkStream;
        var isEncrypted = false;
        var reader = CreateReader(stream);
        var writer = CreateWriter(stream);
        string? loginUser = null;
        string? loginPassword = null;
        var wasEncryptedAtLogin = false;
        string? mailFrom = null;
        var recipients = new List<string>();

        await writer.WriteLineAsync("220 faux-smtp ESMTP");

        while (await reader.ReadLineAsync(stopping.Token) is string line)
        {
            var verb = line.Split(' ', 2)[0].ToUpperInvariant();
            var argument = line.Length > verb.Length ? line[(verb.Length + 1)..] : string.Empty;
            Commands.Enqueue(verb + (isEncrypted ? " chiffré" : " en clair"));

            if (verb == "EHLO")
            {
                await writer.WriteLineAsync("250-faux-smtp");
                if (OffersStartTls && !isEncrypted)
                {
                    await writer.WriteLineAsync("250-STARTTLS");
                }

                await writer.WriteLineAsync("250 AUTH PLAIN");
            }
            else if (verb == "STARTTLS" && OffersStartTls && !isEncrypted)
            {
                await writer.WriteLineAsync("220 2.0.0 Prêt pour TLS");
                var tlsStream = new SslStream(networkStream, false);
                await tlsStream.AuthenticateAsServerAsync(Certificate, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);
                stream = tlsStream;
                isEncrypted = true;
                reader = CreateReader(stream);
                writer = CreateWriter(stream);
            }
            else if (verb == "AUTH")
            {
                var parts = argument.Split(' ', 2);
                var encoded = parts.Length > 1 ? parts[1] : null;
                if (encoded == null)
                {
                    await writer.WriteLineAsync("334 ");
                    encoded = await reader.ReadLineAsync(stopping.Token) ?? string.Empty;
                }

                var fields = Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Split('\0');
                loginUser = fields.Length > 1 ? fields[1] : null;
                loginPassword = fields.Length > 2 ? fields[2] : null;
                wasEncryptedAtLogin = isEncrypted;
                await writer.WriteLineAsync(RejectsLogin ? "535 5.7.8 Identifiants refusés" : "235 2.7.0 Authentifié");
            }
            else if (verb == "MAIL")
            {
                mailFrom = ReadAddress(argument);
                recipients.Clear();
                await writer.WriteLineAsync("250 2.1.0 Ok");
            }
            else if (verb == "RCPT")
            {
                if (RecipientRefusal != null)
                {
                    await writer.WriteLineAsync(RecipientRefusal);
                }
                else
                {
                    recipients.Add(ReadAddress(argument));
                    await writer.WriteLineAsync("250 2.1.5 Ok");
                }
            }
            else if (verb == "DATA")
            {
                await writer.WriteLineAsync("354 Fin des données par <CRLF>.<CRLF>");
                var data = new StringBuilder();
                while (await reader.ReadLineAsync(stopping.Token) is string dataLine && dataLine != ".")
                {
                    data.Append(dataLine.StartsWith("..", StringComparison.Ordinal) ? dataLine[1..] : dataLine).Append("\r\n");
                }

                ReceivedEmails.Enqueue(new ReceivedEmail(loginUser, loginPassword, wasEncryptedAtLogin, mailFrom, recipients.ToList(), data.ToString()));
                await writer.WriteLineAsync("250 2.0.0 Ok : en file");

                if (ClosesAfterAcceptance)
                {
                    return;
                }
            }
            else if (verb == "QUIT")
            {
                OnQuit?.Invoke();
                await writer.WriteLineAsync("221 2.0.0 Au revoir");
                return;
            }
            else if (verb == "RSET" || verb == "NOOP")
            {
                await writer.WriteLineAsync("250 2.0.0 Ok");
            }
            else
            {
                await writer.WriteLineAsync("502 5.5.2 Commande inconnue");
            }
        }
    }

    // Lit l'adresse entre chevrons d'une commande comme « FROM:<adresse> SIZE=123 ».
    private static string ReadAddress(string argument)
    {
        var start = argument.IndexOf('<', StringComparison.Ordinal);
        var end = argument.IndexOf('>', StringComparison.Ordinal);

        return start >= 0 && end > start ? argument[(start + 1)..end] : argument;
    }

    // Lit les lignes du client, en UTF-8.
    private static StreamReader CreateReader(Stream stream)
    {
        return new StreamReader(stream, new UTF8Encoding(false), false, 1024, true);
    }

    // Écrit les réponses du serveur, terminées par CRLF comme le veut SMTP, en UTF-8 sans marque d'ordre des octets.
    private static StreamWriter CreateWriter(Stream stream)
    {
        return new StreamWriter(stream, new UTF8Encoding(false), 1024, true) { NewLine = "\r\n", AutoFlush = true };
    }
}

// Un e-mail accepté par le faux serveur : le compte qui l'a envoyé, si la connexion était chiffrée à ce moment, l'expéditeur et
// les destinataires de l'enveloppe, et le message brut.
public record ReceivedEmail(
    string? LoginUser,
    string? LoginPassword,
    bool WasEncryptedAtLogin,
    string? MailFrom,
    List<string> Recipients,
    string Data);
