using System.Globalization;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Emails;

namespace WalletFlow.Api.Configuration;

// Lit les réglages du serveur SMTP dans les secrets : variables d'environnement en production, jamais le code ni Git (SS §7).
// Sans aucun réglage obligatoire, l'envoi réel n'est pas configuré, et l'API refuse de démarrer hors du développement. Un réglage
// manquant ou illisible l'empêche aussi de démarrer : le message nomme le réglage, jamais sa valeur.
public static class SmtpConfiguration
{
    public const string SectionName = "Smtp";
    public const string HostSetting = SectionName + ":Host";
    public const string PortSetting = SectionName + ":Port";
    public const string UsernameSetting = SectionName + ":Username";
    public const string PasswordSetting = SectionName + ":Password";
    public const string SenderAddressSetting = SectionName + ":SenderAddress";
    public const string SenderNameSetting = SectionName + ":SenderName";

    // Le port de la soumission des e-mails, avec STARTTLS.
    public const int DefaultPort = 587;
    public const string DefaultSenderName = "WalletFlow";

    private static readonly string[] RequiredSettings = { HostSetting, UsernameSetting, PasswordSetting, SenderAddressSetting };

    // Lit le serveur SMTP. Renvoie null si aucun réglage obligatoire n'est donné.
    public static SmtpServerSettings? Read(IConfiguration configuration)
    {
        var givenSettings = RequiredSettings.Where(setting => !string.IsNullOrWhiteSpace(configuration[setting])).ToList();
        if (givenSettings.Count == 0)
        {
            return null;
        }

        var missingSetting = RequiredSettings.Except(givenSettings).FirstOrDefault();
        if (missingSetting != null)
        {
            throw new InvalidOperationException(
                "Le réglage « " + missingSetting + " » manque : l'envoi des e-mails n'est configuré qu'à moitié.");
        }

        var host = configuration[HostSetting]!.Trim();
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            throw new InvalidOperationException("Le réglage « " + HostSetting + " » doit être un nom de serveur, comme smtp.exemple.fr.");
        }

        var senderAddress = EmailAddressRules.Normalize(configuration[SenderAddressSetting]!);
        if (!EmailAddressRules.IsValid(senderAddress))
        {
            throw new InvalidOperationException("Le réglage « " + SenderAddressSetting + " » doit être une adresse e-mail.");
        }

        var senderName = configuration[SenderNameSetting];

        return new SmtpServerSettings(
            host,
            ReadPort(configuration[PortSetting]),
            configuration[UsernameSetting]!.Trim(),
            configuration[PasswordSetting]!,
            senderAddress,
            string.IsNullOrWhiteSpace(senderName) ? DefaultSenderName : senderName.Trim());
    }

    // Lit le port : 587 s'il n'est pas donné, sinon un nombre de 1 à 65535.
    private static int ReadPort(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DefaultPort;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port < 1 || port > 65535)
        {
            throw new InvalidOperationException("Le réglage « " + PortSetting + " » doit être un numéro de port, de 1 à 65535.");
        }

        return port;
    }
}
