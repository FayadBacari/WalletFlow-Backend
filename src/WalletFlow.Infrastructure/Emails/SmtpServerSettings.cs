using MailKit.Security;

namespace WalletFlow.Infrastructure.Emails;

// Le serveur SMTP du prestataire d'e-mails et le compte d'envoi, lus dans les secrets (SS §7 et §9). Ce n'est pas un record : son
// texte affiché ne contient que son type, jamais le mot de passe.
public sealed class SmtpServerSettings
{
    // Le port des connexions chiffrées dès le départ. Tout autre port exige STARTTLS.
    public const int ImplicitTlsPort = 465;

    // Reçoit le serveur, son port, l'identifiant et le mot de passe du compte d'envoi, puis l'adresse et le nom de l'expéditeur.
    public SmtpServerSettings(string host, int port, string username, string password, string senderAddress, string senderName)
    {
        Host = host;
        Port = port;
        Username = username;
        Password = password;
        SenderAddress = senderAddress;
        SenderName = senderName;
    }

    public string Host { get; }
    public int Port { get; }
    public string Username { get; }
    public string Password { get; }
    public string SenderAddress { get; }
    public string SenderName { get; }

    // Le chiffrement exigé : dès la connexion sur le port 465, par STARTTLS sur les autres. Jamais en clair : un serveur qui ne
    // propose pas STARTTLS est refusé avant que le mot de passe parte.
    public SecureSocketOptions Security => Port == ImplicitTlsPort ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
}
