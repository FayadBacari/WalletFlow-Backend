using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles que le compte garde lui-même : confirmation, mot de passe et double authentification (GR Auth, DD §3).
public class UserAccountRulesTests
{
    [Fact]
    public void ConfirmationOfTheEmailAddressMakesTheAccountActive()
    {
        var account = new User { Email = "utilisateur@exemple.test", PasswordHash = "empreinte-de-test" };

        account.ConfirmEmail();

        Assert.True(account.IsEmailConfirmed);
    }

    [Fact]
    public void NewPasswordHashReplacesTheOldOne()
    {
        var account = new User { PasswordHash = "ancienne-empreinte" };

        account.ReplacePasswordHash("nouvelle-empreinte");

        Assert.Equal("nouvelle-empreinte", account.PasswordHash);
    }

    // Une empreinte vide ne correspondrait à aucun mot de passe : le compte serait fermé pour toujours.
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyPasswordHashIsRefusedAndTheOldOneIsKept(string? emptyHash)
    {
        var account = new User { PasswordHash = "ancienne-empreinte" };

        Assert.Throws<ArgumentException>(() => account.ReplacePasswordHash(emptyHash!));
        Assert.Equal("ancienne-empreinte", account.PasswordHash);
    }

    // La contrainte ck_utilisateur_totp de la base demande que l'activation et le secret aillent toujours ensemble.
    [Fact]
    public void TwoFactorIsActivatedWithItsSecretAndDeactivatedWithoutIt()
    {
        var account = new User();

        account.ActivateTwoFactor("secret-chiffre");
        Assert.True(account.IsTwoFactorEnabled);
        Assert.Equal("secret-chiffre", account.EncryptedTotpSecret);

        account.DeactivateTwoFactor();
        Assert.False(account.IsTwoFactorEnabled);
        Assert.Null(account.EncryptedTotpSecret);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void TwoFactorCannotBeActivatedWithoutSecret(string? emptySecret)
    {
        var account = new User();

        Assert.Throws<ArgumentException>(() => account.ActivateTwoFactor(emptySecret!));
        Assert.False(account.IsTwoFactorEnabled);
        Assert.Null(account.EncryptedTotpSecret);
    }
}
