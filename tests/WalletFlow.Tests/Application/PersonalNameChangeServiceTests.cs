using WalletFlow.Application.AccountSettings;

namespace WalletFlow.Tests.Application;

// Vérifie le changement du nom et du prénom, et la lecture du profil (GR Compte), avec de faux outils.
public class PersonalNameChangeServiceTests
{
    private readonly TwoFactorServicesWithFakeTools tools = new TwoFactorServicesWithFakeTools();

    // Change le nom et le prénom du compte.
    private async Task<PersonalNameChangeResult> ChangeNameAsync(Guid userId, string? lastName, string? firstName)
    {
        return await AccountSettingsServices.CreatePersonalNameChange(tools).ChangeAsync(userId, lastName, firstName);
    }

    // Aucun mot de passe n'est demandé : ce n'est pas une action sensible.
    [Fact]
    public async Task NameAndFirstNameAreSavedTrimmedAndNormalized()
    {
        var account = tools.AddAccountWithoutTwoFactor();

        var result = await ChangeNameAsync(account.Id, "  Dupont ", "E\u0301lise");

        Assert.IsType<PersonalNameChangeResult.Changed>(result);
        Assert.Equal("Dupont", account.LastName);
        Assert.Equal("Élise", account.FirstName);
        Assert.Equal(0, tools.PasswordHasher.VerifiedPasswordCount);
        Assert.Equal(1, tools.UnitOfWork.CommittedTransactionCount);
    }

    // GR Compte : l'utilisateur peut effacer son nom et son prénom.
    [Fact]
    public async Task EmptyFieldsEraseTheNameAndTheFirstName()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        await ChangeNameAsync(account.Id, "Dupont", "Élise");

        var result = await ChangeNameAsync(account.Id, "", "   ");

        Assert.IsType<PersonalNameChangeResult.Changed>(result);
        Assert.Null(account.LastName);
        Assert.Null(account.FirstName);
    }

    [Fact]
    public async Task BothInvalidFieldsAreReportedAndNothingChanges()
    {
        var account = tools.AddAccountWithoutTwoFactor();
        await ChangeNameAsync(account.Id, "Dupont", "Élise");

        var result = await ChangeNameAsync(account.Id, new string('a', 51), "Éli\nse");

        var refusal = Assert.IsType<PersonalNameChangeResult.RefusedByRules>(result);
        Assert.True(refusal.IsLastNameInvalid);
        Assert.True(refusal.IsFirstNameInvalid);
        Assert.Equal("Dupont", account.LastName);
        Assert.Equal("Élise", account.FirstName);
        Assert.Equal(1, tools.UnitOfWork.CommittedTransactionCount);
    }

    [Fact]
    public async Task OnlyTheInvalidFieldIsReported()
    {
        var account = tools.AddAccountWithoutTwoFactor();

        var result = await ChangeNameAsync(account.Id, "Dupont", "Éli\u200Bse");

        var refusal = Assert.IsType<PersonalNameChangeResult.RefusedByRules>(result);
        Assert.False(refusal.IsLastNameInvalid);
        Assert.True(refusal.IsFirstNameInvalid);
        Assert.Null(account.LastName);
    }

    [Fact]
    public async Task VanishedAccountIsNotSaved()
    {
        var result = await ChangeNameAsync(Guid.NewGuid(), "Dupont", "Élise");

        Assert.IsType<PersonalNameChangeResult.NotSaved>(result);
        Assert.Equal(0, tools.UnitOfWork.CommittedTransactionCount);
    }

    // Le profil donne l'adresse, le nom, le prénom et l'état de la double authentification, sans aucun secret.
    [Fact]
    public async Task ProfileGivesTheAccountDataOfItsOwner()
    {
        var activatedAccount = tools.AddActivatedAccount();
        await ChangeNameAsync(activatedAccount.UserAccount.Id, "Dupont", "Élise");

        var profile = await AccountSettingsServices.CreateProfile(tools).FindAsync(activatedAccount.UserAccount.Id);

        Assert.NotNull(profile);
        Assert.Equal(TwoFactorServicesWithFakeTools.AccountEmail, profile.Email);
        Assert.Equal("Dupont", profile.LastName);
        Assert.Equal("Élise", profile.FirstName);
        Assert.True(profile.IsTwoFactorEnabled);
    }

    [Fact]
    public async Task ProfileOfVanishedAccountIsNull()
    {
        var profile = await AccountSettingsServices.CreateProfile(tools).FindAsync(Guid.NewGuid());

        Assert.Null(profile);
    }
}
