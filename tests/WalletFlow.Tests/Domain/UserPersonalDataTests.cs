using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie que le compte ne change de nom, de prénom et d'adresse que selon leurs règles (GR Compte).
public class UserPersonalDataTests
{
    // Crée un compte avec un nom, un prénom et une adresse.
    private static User CreateAccount()
    {
        return new User
        {
            Email = "ancienne@exemple.test",
            PasswordHash = "empreinte-de-test",
            LastName = "Ancien",
            FirstName = "Prénom"
        };
    }

    [Fact]
    public void NameAndFirstNameAreReplacedThenErased()
    {
        var account = CreateAccount();

        account.ReplaceName("Dupont", "Élise");
        Assert.Equal("Dupont", account.LastName);
        Assert.Equal("Élise", account.FirstName);

        account.ReplaceName(null, null);
        Assert.Null(account.LastName);
        Assert.Null(account.FirstName);
    }

    // Un nom vide n'efface pas : seul un nom absent efface, et la base refuserait un nom vide.
    [Theory]
    [InlineData("", "Élise")]
    [InlineData("Dupont", "Éli\nse")]
    [InlineData("Dupont", "   ")]
    public void NameBreakingTheRulesIsRefusedAndNothingChanges(string? lastName, string? firstName)
    {
        var account = CreateAccount();

        Assert.Throws<ArgumentException>(() => account.ReplaceName(lastName, firstName));

        Assert.Equal("Ancien", account.LastName);
        Assert.Equal("Prénom", account.FirstName);
    }

    [Fact]
    public void EmailIsReplacedByTheNewConfirmedAddress()
    {
        var account = CreateAccount();

        account.ReplaceEmail("nouvelle@exemple.test");

        Assert.Equal("nouvelle@exemple.test", account.Email);
    }

    // L'adresse arrive déjà en minuscules et sans espaces : une autre forme veut dire qu'une règle a été oubliée en chemin.
    [Theory]
    [InlineData("Nouvelle@Exemple.test")]
    [InlineData(" nouvelle@exemple.test")]
    [InlineData("adresse-sans-arobase")]
    [InlineData("")]
    public void EmailNotInItsSavedFormIsRefusedAndNothingChanges(string newEmail)
    {
        var account = CreateAccount();

        Assert.Throws<ArgumentException>(() => account.ReplaceEmail(newEmail));

        Assert.Equal("ancienne@exemple.test", account.Email);
    }
}
