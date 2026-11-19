using System.Globalization;
using System.Net.Sockets;
using System.Security.Authentication;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Text;
using WalletFlow.Application.Emails;

namespace WalletFlow.Infrastructure.Emails;

// Envoie les e-mails par le serveur SMTP du prestataire, toujours chiffré avec TLS 1.2 au moins, avec le compte d'envoi
// (SS §7 et §9). Une connexion par e-mail : la file en envoie un à la fois. Un envoi qui échoue lève
// EmailCouldNotBeSentException, et la file réessaie plus tard. Le journal technique nomme l'erreur, jamais l'adresse du
// destinataire ni le texte du serveur, qui peut la contenir (SS §7).
public sealed class SmtpEmailSender : IEmailSender
{
    // Au-delà de ce temps sans réponse du serveur, l'essai échoue : la file réessaiera.
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    // TLS 1.2 au moins (SS §7).
    public const SslProtocols AllowedProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;

    private readonly SmtpServerSettings server;
    private readonly Func<SmtpClient> createClient;
    private readonly ILogger<SmtpEmailSender> logger;

    // Reçoit le serveur et le journal technique : chaque envoi crée son client SMTP avec les réglages de CreateClient.
    public SmtpEmailSender(SmtpServerSettings server, ILogger<SmtpEmailSender> logger)
        : this(server, CreateClient, logger)
    {
    }

    // Reçoit aussi la création du client SMTP : un test peut y faire confiance au certificat de son propre faux serveur.
    public SmtpEmailSender(SmtpServerSettings server, Func<SmtpClient> createClient, ILogger<SmtpEmailSender> logger)
    {
        this.server = server;
        this.createClient = createClient;
        this.logger = logger;
    }

    // Crée le client SMTP des envois : TLS 1.2 au moins, certificat du serveur vérifié par le système, attente bornée.
    public static SmtpClient CreateClient()
    {
        return new SmtpClient
        {
            Timeout = (int)Timeout.TotalMilliseconds,
            SslProtocols = AllowedProtocols
        };
    }

    // Envoie l'e-mail : connexion chiffrée, authentification, puis envoi. Une fois l'e-mail accepté par le serveur, la fin de la
    // connexion ne peut plus faire échouer l'envoi : sinon, la file le renverrait en double.
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        using var client = createClient();

        try
        {
            var mimeMessage = CreateMimeMessage(message);
            await client.ConnectAsync(server.Host, server.Port, server.Security, cancellationToken);
            await client.AuthenticateAsync(server.Username, server.Password, cancellationToken);
            await client.SendAsync(mimeMessage, cancellationToken);
        }
        catch (Exception exception) when (IsSendingFailure(exception, cancellationToken))
        {
            logger.LogWarning("L'envoi d'un e-mail par SMTP a échoué : {Erreur}.", Describe(exception));
            throw new EmailCouldNotBeSentException(exception);
        }

        // L'e-mail est accepté : la fin de la connexion ne peut plus faire échouer l'envoi. MailKit ne lève aucune erreur en la
        // terminant, même coupée ou abrégée par l'arrêt de l'API (voir les tests de SmtpEmailSender).
        await client.DisconnectAsync(true, cancellationToken);
    }

    // Écrit l'e-mail au format MIME : l'expéditeur des réglages, le destinataire, l'objet et le texte brut.
    private MimeMessage CreateMimeMessage(EmailMessage message)
    {
        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(server.SenderName, server.SenderAddress));
        mimeMessage.To.Add(new MailboxAddress(string.Empty, message.RecipientAddress));
        mimeMessage.Subject = message.Subject;
        mimeMessage.Body = new TextPart(TextFormat.Plain) { Text = message.Body };

        return mimeMessage;
    }

    // Vrai si l'exception est une panne de l'envoi. Un délai dépassé en est une ; l'arrêt de l'API ou l'abandon de la requête
    // n'en est pas une : l'annulation remonte telle quelle.
    private static bool IsSendingFailure(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException)
        {
            return !cancellationToken.IsCancellationRequested;
        }

        return IsTransportFailure(exception);
    }

    // Vrai si l'exception vient du serveur, du réseau ou du chiffrement : refus du serveur, protocole, authentification, TLS,
    // connexion coupée, délai, extension absente (comme STARTTLS), adresse illisible.
    private static bool IsTransportFailure(Exception exception)
    {
        return exception is CommandException
            or ProtocolException
            or MailKit.Security.AuthenticationException
            or System.Security.Authentication.AuthenticationException
            or SslHandshakeException
            or ServiceNotConnectedException
            or ServiceNotAuthenticatedException
            or IOException
            or SocketException
            or TimeoutException
            or NotSupportedException
            or ParseException;
    }

    // Décrit l'erreur pour le journal technique : son type et, pour un refus du serveur, ses codes. Jamais le texte du serveur,
    // qui peut contenir l'adresse du destinataire (SS §7).
    private static string Describe(Exception exception)
    {
        if (exception is SmtpCommandException commandException)
        {
            return exception.GetType().Name + " " + commandException.ErrorCode + " "
                + ((int)commandException.StatusCode).ToString(CultureInfo.InvariantCulture);
        }

        return exception.GetType().Name;
    }
}
