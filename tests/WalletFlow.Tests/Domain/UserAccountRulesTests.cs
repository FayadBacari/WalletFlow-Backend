using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

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

    // La contrainte ck_utilisateur_totp de la base demande que l'activation et le secret aillent toujours ensemble, et
    // ck_utilisateur_pas_totp qu'un pas de code n'existe qu'avec eux.
    [Fact]
    public void TwoFactorIsActivatedWithItsSecretAndDeactivatedWithoutIt()
    {
        var account = new User();

        account.ActivateTwoFactor("secret-chiffre", 1000);
        Assert.True(account.IsTwoFactorEnabled);
        Assert.Equal("secret-chiffre", account.EncryptedTotpSecret);
        Assert.Equal(1000, account.LastAuthenticatorStep);

        account.DeactivateTwoFactor();
        Assert.False(account.IsTwoFactorEnabled);
        Assert.Null(account.EncryptedTotpSecret);
        Assert.Null(account.LastAuthenticatorStep);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TwoFactorCannotBeActivatedWithoutAPositiveFirstStep(long firstStep)
    {
        var account = new User();

        Assert.Throws<ArgumentOutOfRangeException>(() => account.ActivateTwoFactor("secret-chiffre", firstStep));
        Assert.False(account.IsTwoFactorEnabled);
    }

    // SS §2 : un code de l'application ne sert qu'une fois. Le code de l'activation lui-même ne resert pas ; un code plus ancien
    // non plus, même jamais utilisé ; seul un pas plus récent passe.
    [Fact]
    public void AuthenticatorStepServesOnceAndNeverGoesBack()
    {
        var account = new User();
        account.ActivateTwoFactor("secret-chiffre", 1000);

        Assert.False(account.CanUseAuthenticatorStep(1000));
        Assert.False(account.CanUseAuthenticatorStep(999));
        Assert.True(account.CanUseAuthenticatorStep(1001));

        account.UseAuthenticatorStep(1001);

        Assert.Equal(1001, account.LastAuthenticatorStep);
        Assert.False(account.CanUseAuthenticatorStep(1001));
        Assert.Throws<InvalidOperationException>(() => account.UseAuthenticatorStep(1001));
        Assert.Throws<InvalidOperationException>(() => account.UseAuthenticatorStep(1000));
        Assert.Equal(1001, account.LastAuthenticatorStep);
    }

    // Sans double authentification, aucun code de l'application ne sert : il n'y a pas de secret pour le vérifier.
    [Fact]
    public void AccountWithoutTwoFactorCannotUseAnyStep()
    {
        var account = new User();

        Assert.False(account.CanUseAuthenticatorStep(1000));
        Assert.Throws<InvalidOperationException>(() => account.UseAuthenticatorStep(1000));
        Assert.Null(account.LastAuthenticatorStep);
    }

    // SS §7 : le même secret chiffré avec une autre clé remplace l'ancien texte ; le pas du dernier code reste valable.
    [Fact]
    public void EncryptedSecretIsReplacedWithoutChangingTheLastStep()
    {
        var account = new User();
        account.ActivateTwoFactor("secret-chiffre-avec-la-cle-1", 1000);

        account.ReplaceEncryptedTotpSecret("secret-chiffre-avec-la-cle-2");

        Assert.Equal("secret-chiffre-avec-la-cle-2", account.EncryptedTotpSecret);
        Assert.Equal(1000, account.LastAuthenticatorStep);
        Assert.True(account.IsTwoFactorEnabled);
    }

    // Sans double authentification, il n'y a pas de secret à remplacer ; un texte vide ne remplace rien.
    [Fact]
    public void EncryptedSecretCannotBeReplacedWithoutTwoFactorOrByAnEmptyText()
    {
        var withoutTwoFactor = new User();
        var withTwoFactor = new User();
        withTwoFactor.ActivateTwoFactor("secret-chiffre", 1000);

        Assert.Throws<InvalidOperationException>(() => withoutTwoFactor.ReplaceEncryptedTotpSecret("secret-chiffre"));
        Assert.Throws<ArgumentException>(() => withTwoFactor.ReplaceEncryptedTotpSecret(""));
        Assert.Null(withoutTwoFactor.EncryptedTotpSecret);
        Assert.Equal("secret-chiffre", withTwoFactor.EncryptedTotpSecret);
    }

    // Un nouveau secret repart sans passé : le pas de l'ancien secret ne bloque pas le premier code du nouveau.
    [Fact]
    public void NewSecretStartsWithItsOwnFirstStep()
    {
        var account = new User();
        account.ActivateTwoFactor("ancien-secret", 2000);
        account.UseAuthenticatorStep(2001);
        account.DeactivateTwoFactor();

        account.ActivateTwoFactor("nouveau-secret", 2001);

        Assert.Equal(2001, account.LastAuthenticatorStep);
        Assert.True(account.CanUseAuthenticatorStep(2002));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void TwoFactorCannotBeActivatedWithoutSecret(string? emptySecret)
    {
        var account = new User();

        Assert.Throws<ArgumentException>(() => account.ActivateTwoFactor(emptySecret!, TwoFactorTestTools.OldStep));
        Assert.False(account.IsTwoFactorEnabled);
        Assert.Null(account.EncryptedTotpSecret);
    }
}
