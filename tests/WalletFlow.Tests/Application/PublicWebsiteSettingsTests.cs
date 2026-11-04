using WalletFlow.Application.Configuration;

namespace WalletFlow.Tests.Application;

// Vérifie le réglage de l'adresse du site : les liens envoyés par e-mail restent en HTTPS (SS §9).
public class PublicWebsiteSettingsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingWebsiteAddressIsRefusedAtStartup(string? missingWebsiteAddressForThisTest)
    {
        Assert.Throws<InvalidOperationException>(() => new PublicWebsiteSettings(missingWebsiteAddressForThisTest));
    }

    [Theory]
    [InlineData("http://walletflow.test")]
    [InlineData("walletflow.test")]
    public void WebsiteAddressWithoutHttpsIsRefusedAtStartup(string websiteAddressWithoutHttpsForThisTest)
    {
        Assert.Throws<InvalidOperationException>(() => new PublicWebsiteSettings(websiteAddressWithoutHttpsForThisTest));
    }

    [Fact]
    public void FinalSlashOfTheWebsiteAddressIsRemoved()
    {
        var settingsOfThePublicWebsiteBeingTested = new PublicWebsiteSettings("https://walletflow.test/");

        Assert.Equal("https://walletflow.test", settingsOfThePublicWebsiteBeingTested.PublicWebsiteAddress);
    }
}
