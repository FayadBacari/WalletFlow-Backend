using Microsoft.Extensions.Configuration;
using WalletFlow.Api.Configuration;

namespace WalletFlow.Tests.Api;

// Vérifie la lecture des clés qui chiffrent les secrets de double authentification (SS §2 et §7).
public class TotpEncryptionSettingsTests
{
    // Lit ces réglages.
    private static TotpEncryptionSettings Read(Dictionary<string, string?> settings)
    {
        return TotpEncryptionSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    // Sans numéro, la clé actuelle est la clé 1, sans ancienne clé.
    [Fact]
    public void SingleKeyIsKeyOne()
    {
        var settings = Read(new Dictionary<string, string?> { ["TwoFactorSecretEncryptionKey"] = "cle-actuelle" });

        Assert.Equal(1, settings.CurrentKeyId);
        Assert.Equal("cle-actuelle", settings.CurrentKey);
        Assert.Empty(settings.PreviousKeys);
    }

    [Fact]
    public void NewKeyIsReadWithItsNumberAndTheOldKeys()
    {
        var settings = Read(new Dictionary<string, string?>
        {
            ["TwoFactorSecretEncryptionKey"] = "cle-actuelle",
            ["TwoFactorSecretEncryptionKeyId"] = "3",
            ["TwoFactorSecretPreviousKeys:1"] = "cle-1",
            ["TwoFactorSecretPreviousKeys:2"] = "cle-2"
        });

        Assert.Equal(3, settings.CurrentKeyId);
        Assert.Equal(new Dictionary<int, string> { [1] = "cle-1", [2] = "cle-2" }, settings.PreviousKeys);
    }

    // Un numéro illisible empêche l'API de démarrer, sans jamais citer une clé.
    [Theory]
    [InlineData("TwoFactorSecretEncryptionKeyId", "deux")]
    [InlineData("TwoFactorSecretEncryptionKeyId", "0")]
    [InlineData("TwoFactorSecretEncryptionKeyId", "-1")]
    [InlineData("TwoFactorSecretEncryptionKeyId", " 2")]
    [InlineData("TwoFactorSecretPreviousKeys:ancienne", "cle-ancienne")]
    [InlineData("TwoFactorSecretPreviousKeys:0", "cle-ancienne")]
    public void UnreadableKeyNumberIsRefusedAtStartup(string settingName, string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Read(new Dictionary<string, string?>
        {
            ["TwoFactorSecretEncryptionKey"] = "cle-actuelle",
            [settingName] = value
        }));

        Assert.DoesNotContain("cle-actuelle", exception.Message);
        Assert.DoesNotContain("cle-ancienne", exception.Message);
    }
}
