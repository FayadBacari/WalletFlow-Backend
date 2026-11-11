using WalletFlow.Application.Configuration;

namespace WalletFlow.Tests.Application;

// Vérifie le réglage de l'adresse du site : les liens envoyés par e-mail restent en HTTPS (SS §9).
public class PublicWebsiteSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingWebsiteAddressIsRefusedAtStartup(string? missingAddress)
    {
        Assert.Throws<InvalidOperationException>(() => new PublicWebsiteSettings(missingAddress));
    }

    [Theory]
    [InlineData("http://walletflow.test")]
    [InlineData("walletflow.test")]
    public void WebsiteAddressWithoutHttpsIsRefusedAtStartup(string httpAddress)
    {
        Assert.Throws<InvalidOperationException>(() => new PublicWebsiteSettings(httpAddress));
    }

    // Le jeton est après le # : il n'est jamais envoyé au serveur quand la page s'ouvre (SS §7).
    [Fact]
    public void LinkContainsThePageAndTheTokenAfterTheHashSign()
    {
        var settings = new PublicWebsiteSettings("https://walletflow.test/");

        var link = settings.BuildLink("/reset-password", "jeton-du-lien");

        Assert.Equal("https://walletflow.test/reset-password#token=jeton-du-lien", link);
    }

    [Fact]
    public void FinalSlashOfTheWebsiteAddressIsRemoved()
    {
        var settings = new PublicWebsiteSettings("https://walletflow.test/");

        Assert.Equal("https://walletflow.test", settings.PublicWebsiteAddress);
    }
}
