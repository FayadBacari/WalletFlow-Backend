using WalletFlow.Application.Configuration;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le cas d'utilisation de l'inscription (GR Auth, SS §2), avec de faux outils.
public class AccountRegistrationServiceTests
{
    private const string EmailAddressTypedByTheVisitorInTheseTests = " Nouvel.Utilisateur@Exemple.test ";
    private const string EmailAddressInStoredFormInTheseTests = "nouvel.utilisateur@exemple.test";
    private const string ValidPasswordChosenInTheseTests = "Mot-De-Passe-Valide-1";

    private readonly FakeUserAccountRepository repositoryThatKeepsAccountsInMemory = new FakeUserAccountRepository();
    private readonly FakePasswordHasher hasherThatCountsTheHashedPasswords = new FakePasswordHasher();
    private readonly FakeEmailSender senderThatKeepsTheEmailsInMemory = new FakeEmailSender();

    // Crée le service à tester, avec ses faux outils.
    private AccountRegistrationService CreateRegistrationServiceWithFakeTools()
    {
        return new AccountRegistrationService(
            repositoryThatKeepsAccountsInMemory,
            hasherThatCountsTheHashedPasswords,
            new FakeSecureTokenGenerator(),
            senderThatKeepsTheEmailsInMemory,
            new PublicWebsiteSettings("https://walletflow.test/"));
    }

    [Fact]
    public async Task NewAccountIsSavedWithItsStoredEmailAndItsHashedPassword()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();

        var resultOfTheVisitorAccountRegistration = await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        Assert.True(resultOfTheVisitorAccountRegistration.IsAccepted);
        var userSavedByTheRegistrationService = Assert.Single(repositoryThatKeepsAccountsInMemory.SavedUsers);
        Assert.Equal(EmailAddressInStoredFormInTheseTests, userSavedByTheRegistrationService.Email);
        Assert.Equal(FakePasswordHasher.PrefixOfTheFakePasswordHashes + ValidPasswordChosenInTheseTests, userSavedByTheRegistrationService.PasswordHash);
        Assert.Equal(UserRole.User, userSavedByTheRegistrationService.Role);
        Assert.False(userSavedByTheRegistrationService.IsEmailConfirmed);
    }

    [Fact]
    public async Task NewAccountReceivesItsEightCategoriesAndItsConfirmationToken()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();

        await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        var userSavedByTheRegistrationService = Assert.Single(repositoryThatKeepsAccountsInMemory.SavedUsers);
        Assert.Equal(8, repositoryThatKeepsAccountsInMemory.SavedCategories.Count);

        // Chaque catégorie appartient bien au nouvel utilisateur.
        foreach (var categorySavedByTheRegistrationService in repositoryThatKeepsAccountsInMemory.SavedCategories)
        {
            Assert.Equal(userSavedByTheRegistrationService.Id, categorySavedByTheRegistrationService.UserId);
        }

        // Seule l'empreinte du jeton va en base, jamais le jeton lui-même (SS §2).
        var tokenSavedByTheRegistrationService = Assert.Single(repositoryThatKeepsAccountsInMemory.SavedEmailTokens);
        Assert.Equal(EmailTokenType.SignupConfirmation, tokenSavedByTheRegistrationService.Type);
        Assert.Equal(FakeSecureTokenGenerator.HashAlwaysReturnedByThisFakeGenerator, tokenSavedByTheRegistrationService.TokenHash);
        Assert.Equal(userSavedByTheRegistrationService.Id, tokenSavedByTheRegistrationService.UserId);

        // Le lien de confirmation est valable 24 heures.
        var durationLeftBeforeTheTokenExpires = tokenSavedByTheRegistrationService.ExpiresAt - DateTime.UtcNow;
        Assert.InRange(durationLeftBeforeTheTokenExpires, TimeSpan.FromHours(23.9), TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task NewAccountReceivesConfirmationEmailContainingTheTokenLink()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();

        await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        var emailSentByTheRegistrationService = Assert.Single(senderThatKeepsTheEmailsInMemory.SentEmails);
        Assert.Equal(EmailAddressInStoredFormInTheseTests, emailSentByTheRegistrationService.RecipientAddress);
        Assert.Contains(
            "https://walletflow.test/confirm-email#token=" + FakeSecureTokenGenerator.TokenAlwaysReturnedByThisFakeGenerator,
            emailSentByTheRegistrationService.Body);

        // L'e-mail annonce la vraie durée de validité du lien (SS §2).
        Assert.Contains("valable 24 heures", emailSentByTheRegistrationService.Body);

        // Un e-mail ne contient jamais de mot de passe (SS §9).
        Assert.DoesNotContain(ValidPasswordChosenInTheseTests, emailSentByTheRegistrationService.Body);
    }

    [Fact]
    public async Task AddressAlreadyUsedSavesNothingAndWarnsItsOwner()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();
        repositoryThatKeepsAccountsInMemory.SavedUsers.Add(new User { Email = EmailAddressInStoredFormInTheseTests });

        var resultOfTheVisitorAccountRegistration = await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        // La réponse reste la même : seul le propriétaire de l'adresse apprend qu'elle est déjà utilisée.
        Assert.True(resultOfTheVisitorAccountRegistration.IsAccepted);
        Assert.Single(repositoryThatKeepsAccountsInMemory.SavedUsers);
        Assert.Empty(repositoryThatKeepsAccountsInMemory.SavedEmailTokens);
        var emailSentByTheRegistrationService = Assert.Single(senderThatKeepsTheEmailsInMemory.SentEmails);
        Assert.Equal(EmailAddressInStoredFormInTheseTests, emailSentByTheRegistrationService.RecipientAddress);
        Assert.DoesNotContain("confirm-email", emailSentByTheRegistrationService.Body);
    }

    [Fact]
    public async Task AddressAlreadyUsedStillHashesThePasswordToKeepTheSameDuration()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();
        repositoryThatKeepsAccountsInMemory.SavedUsers.Add(new User { Email = EmailAddressInStoredFormInTheseTests });

        await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        Assert.Equal(1, hasherThatCountsTheHashedPasswords.NumberOfPasswordsHashed);
    }

    [Fact]
    public async Task AddressTakenJustBeforeTheSaveWarnsItsOwnerInstead()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();
        repositoryThatKeepsAccountsInMemory.AddressIsTakenJustBeforeTheSave = true;

        var resultOfTheVisitorAccountRegistration = await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        Assert.True(resultOfTheVisitorAccountRegistration.IsAccepted);
        var emailSentByTheRegistrationService = Assert.Single(senderThatKeepsTheEmailsInMemory.SentEmails);
        Assert.DoesNotContain("confirm-email", emailSentByTheRegistrationService.Body);
    }

    [Fact]
    public async Task InvalidEmailAddressIsRefusedWithoutSavingOrSendingAnything()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();

        var resultOfTheVisitorAccountRegistration = await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            "adresse-sans-arobase", ValidPasswordChosenInTheseTests);

        Assert.False(resultOfTheVisitorAccountRegistration.IsAccepted);
        Assert.Equal(AccountRegistrationService.ErrorMessageForInvalidEmailAddressFormat, resultOfTheVisitorAccountRegistration.EmailAddressErrorMessage);
        Assert.Null(resultOfTheVisitorAccountRegistration.PasswordErrorMessage);
        Assert.Empty(repositoryThatKeepsAccountsInMemory.SavedUsers);
        Assert.Empty(senderThatKeepsTheEmailsInMemory.SentEmails);
    }

    [Fact]
    public async Task PasswordBreakingTheRulesIsRefusedWithoutHashingOrSavingAnything()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();

        var resultOfTheVisitorAccountRegistration = await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, "trop-court");

        Assert.False(resultOfTheVisitorAccountRegistration.IsAccepted);
        Assert.Equal(AccountRegistrationService.ErrorMessageForPasswordBreakingTheRules, resultOfTheVisitorAccountRegistration.PasswordErrorMessage);
        Assert.Null(resultOfTheVisitorAccountRegistration.EmailAddressErrorMessage);
        Assert.Equal(0, hasherThatCountsTheHashedPasswords.NumberOfPasswordsHashed);
        Assert.Empty(repositoryThatKeepsAccountsInMemory.SavedUsers);
        Assert.Empty(senderThatKeepsTheEmailsInMemory.SentEmails);
    }

    [Fact]
    public async Task BothErrorsAreReturnedTogetherWhenEmailAndPasswordAreInvalid()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();

        var resultOfTheVisitorAccountRegistration = await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            "adresse-sans-arobase", "trop-court");

        Assert.False(resultOfTheVisitorAccountRegistration.IsAccepted);
        Assert.NotNull(resultOfTheVisitorAccountRegistration.EmailAddressErrorMessage);
        Assert.NotNull(resultOfTheVisitorAccountRegistration.PasswordErrorMessage);
    }
}
