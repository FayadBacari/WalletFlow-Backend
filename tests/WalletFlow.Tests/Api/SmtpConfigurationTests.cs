using MailKit.Security;
using Microsoft.Extensions.Configuration;
using WalletFlow.Api.Configuration;

namespace WalletFlow.Tests.Api;

// Vérifie la lecture des réglages du serveur SMTP (SS §7 et §9) : tout ou rien, valeurs vérifiées au démarrage, chiffrement
// toujours exigé, et aucun message d'erreur qui recopie le mot de passe.
public class SmtpConfigurationTests
{
    private const string Password = "Exemple-De-Mot-De-Passe-8";

    // Crée une configuration avec les réglages complets, puis applique ces changements : une valeur vide retire le réglage.
    private static IConfiguration CreateConfiguration(params (string Name, string Value)[] changes)
    {
        var settings = new Dictionary<string, string?>
        {
            [SmtpConfiguration.HostSetting] = "smtp.prestataire.test",
            [SmtpConfiguration.UsernameSetting] = "compte-envoi",
            [SmtpConfiguration.PasswordSetting] = Password,
            [SmtpConfiguration.SenderAddressSetting] = "NoReply@WalletFlow.test"
        };

        foreach (var (name, value) in changes)
        {
            settings[name] = value == "" ? null : value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    // Sans aucun réglage, l'envoi réel n'est pas configuré.
    [Fact]
    public void NoSettingMeansNotConfigured()
    {
        Assert.Null(SmtpConfiguration.Read(new ConfigurationBuilder().Build()));
    }

    // Les réglages complets donnent le serveur. Le port 587 avec STARTTLS et le nom « WalletFlow » sont pris par défaut, et
    // l'adresse de l'expéditeur est écrite en minuscules, comme toutes les adresses de WalletFlow.
    [Fact]
    public void CompleteSettingsGiveTheServerWithItsDefaults()
    {
        var server = Assert.IsType<WalletFlow.Infrastructure.Emails.SmtpServerSettings>(SmtpConfiguration.Read(CreateConfiguration()));

        Assert.Equal(
            ("smtp.prestataire.test", 587, "compte-envoi", Password, "noreply@walletflow.test", "WalletFlow", SecureSocketOptions.StartTls),
            (server.Host, server.Port, server.Username, server.Password, server.SenderAddress, server.SenderName, server.Security));
        Assert.DoesNotContain(Password, server.ToString(), StringComparison.Ordinal);
    }

    // Le port 465 chiffre dès la connexion ; tous les autres exigent STARTTLS. Aucun ne permet d'envoyer en clair.
    [Theory]
    [InlineData("465", SecureSocketOptions.SslOnConnect)]
    [InlineData("587", SecureSocketOptions.StartTls)]
    [InlineData("25", SecureSocketOptions.StartTls)]
    [InlineData("2525", SecureSocketOptions.StartTls)]
    public void EveryPortRequiresEncryption(string port, SecureSocketOptions expectedSecurity)
    {
        var server = SmtpConfiguration.Read(CreateConfiguration((SmtpConfiguration.PortSetting, port)));

        Assert.Equal(expectedSecurity, server?.Security);
    }

    // Un réglage obligatoire qui manque empêche l'API de démarrer : le message le nomme, sans jamais recopier le mot de passe.
    [Theory]
    [InlineData(SmtpConfiguration.HostSetting)]
    [InlineData(SmtpConfiguration.UsernameSetting)]
    [InlineData(SmtpConfiguration.PasswordSetting)]
    [InlineData(SmtpConfiguration.SenderAddressSetting)]
    public void MissingRequiredSettingIsRefused(string missingSetting)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => SmtpConfiguration.Read(CreateConfiguration((missingSetting, ""))));

        Assert.Contains(missingSetting, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, exception.Message, StringComparison.Ordinal);
    }

    // Un port, un nom de serveur ou une adresse d'expéditeur illisible empêche l'API de démarrer, en nommant le réglage.
    [Theory]
    [InlineData(SmtpConfiguration.PortSetting, "0")]
    [InlineData(SmtpConfiguration.PortSetting, "65536")]
    [InlineData(SmtpConfiguration.PortSetting, "-1")]
    [InlineData(SmtpConfiguration.PortSetting, "587 ")]
    [InlineData(SmtpConfiguration.PortSetting, "abc")]
    [InlineData(SmtpConfiguration.HostSetting, "smtp prestataire.test")]
    [InlineData(SmtpConfiguration.HostSetting, "https://smtp.prestataire.test")]
    [InlineData(SmtpConfiguration.SenderAddressSetting, "pas-une-adresse")]
    public void UnreadableSettingIsRefused(string setting, string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => SmtpConfiguration.Read(CreateConfiguration((setting, value))));

        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, exception.Message, StringComparison.Ordinal);
    }
}
