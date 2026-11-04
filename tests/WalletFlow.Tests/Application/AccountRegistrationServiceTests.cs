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
    private const string OldSecureHashOfTheExistingAccount = "ancienne-empreinte-du-compte";

    private readonly FakeUserAccountRepository repositoryThatKeepsAccountsInMemory = new FakeUserAccountRepository();
    private readonly FakePasswordHasher hasherThatCountsTheHashedPasswords = new FakePasswordHasher();
    private readonly FakeEmailSender senderThatKeepsTheEmailsInMemory = new FakeEmailSender();
    private readonly FakeEmailAddressRequestLimiter limiterThatCountsTheEmailAddresses = new FakeEmailAddressRequestLimiter();
    private readonly FakeTimeProviderForTests clockThatOnlyMovesWhenTestsAskIt = new FakeTimeProviderForTests();

    // Crée le service à tester, avec ses faux outils.
    private AccountRegistrationService CreateRegistrationServiceWithFakeTools()
    {
        return new AccountRegistrationService(
            repositoryThatKeepsAccountsInMemory,
            hasherThatCountsTheHashedPasswords,
            new FakeSecureTokenGenerator(),
            senderThatKeepsTheEmailsInMemory,
            new PublicWebsiteSettings("https://walletflow.test/"),
            limiterThatCountsTheEmailAddresses,
            clockThatOnlyMovesWhenTestsAskIt);
    }

    // Le mot de passe est normalisé avant le hachage : tapé sur un autre appareil, il donnera le même résultat (SS §2).
    [Fact]
    public async Task PasswordTypedWithDecomposedAccentIsHashedInItsNormalizedForm()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();

        await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(EmailAddressTypedByTheVisitorInTheseTests, "Mot-De-Passé-1");

        Assert.Equal(FakePasswordHasher.PrefixOfTheFakePasswordHashes + "Mot-De-Passé-1", repositoryThatKeepsAccountsInMemory.SavedUsers[0].PasswordHash);
    }

    // Le mot de passe est normalisé avant les règles : 12 signes sans normalisation, mais 11 vrais caractères (SS §2).
    [Fact]
    public async Task PasswordLengthIsCountedAfterTheNormalization()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();

        var resultOfTheVisitorAccountRegistration = await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(EmailAddressTypedByTheVisitorInTheseTests, "Abcdefgh-1é");

        Assert.False(resultOfTheVisitorAccountRegistration.IsAccepted);
        Assert.Equal(AccountRegistrationService.ErrorMessageForPasswordBreakingTheRules, resultOfTheVisitorAccountRegistration.PasswordErrorMessage);
    }

    // Ajoute à la fausse base un compte qui utilise déjà l'adresse de ces tests, avec son ancien jeton.
    private User AddExistingAccountToTheFakeDatabase(bool emailAddressOfTheAccountIsAlreadyConfirmed)
    {
        var existingAccountAlreadyUsingTheAddress = new User
        {
            Email = EmailAddressInStoredFormInTheseTests,
            PasswordHash = OldSecureHashOfTheExistingAccount,
            IsEmailConfirmed = emailAddressOfTheAccountIsAlreadyConfirmed
        };
        repositoryThatKeepsAccountsInMemory.SavedUsers.Add(existingAccountAlreadyUsingTheAddress);
        repositoryThatKeepsAccountsInMemory.SavedEmailTokens.Add(new EmailToken
        {
            Type = EmailTokenType.SignupConfirmation,
            TokenHash = "ancienne-empreinte-du-jeton",
            UserId = existingAccountAlreadyUsingTheAddress.Id
        });

        return existingAccountAlreadyUsingTheAddress;
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

        // Le lien de confirmation est valable exactement 24 heures.
        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddHours(24), tokenSavedByTheRegistrationService.ExpiresAt);
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

        // L'e-mail dit qu'il faudra saisir le mot de passe, et annonce la vraie durée du lien (GR Auth, SS §2).
        Assert.Contains("saisis le mot de passe", emailSentByTheRegistrationService.Body);
        Assert.Contains("valable 24 heures", emailSentByTheRegistrationService.Body);

        // Un e-mail ne contient jamais de mot de passe (SS §9).
        Assert.DoesNotContain(ValidPasswordChosenInTheseTests, emailSentByTheRegistrationService.Body);
    }

    // Une adresse confirmée ne change jamais : son propriétaire est seulement prévenu (SS §2).
    [Fact]
    public async Task ConfirmedAddressChangesNothingAndWarnsItsOwner()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();
        var existingAccountAlreadyConfirmedInThisTest = AddExistingAccountToTheFakeDatabase(emailAddressOfTheAccountIsAlreadyConfirmed: true);

        var resultOfTheVisitorAccountRegistration = await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        // La réponse reste la même : seul le propriétaire de l'adresse apprend qu'elle est déjà utilisée.
        Assert.True(resultOfTheVisitorAccountRegistration.IsAccepted);
        Assert.Single(repositoryThatKeepsAccountsInMemory.SavedUsers);
        Assert.Equal(OldSecureHashOfTheExistingAccount, existingAccountAlreadyConfirmedInThisTest.PasswordHash);
        Assert.Equal("ancienne-empreinte-du-jeton", Assert.Single(repositoryThatKeepsAccountsInMemory.SavedEmailTokens).TokenHash);
        var emailSentByTheRegistrationService = Assert.Single(senderThatKeepsTheEmailsInMemory.SentEmails);
        Assert.Equal(EmailAddressInStoredFormInTheseTests, emailSentByTheRegistrationService.RecipientAddress);
        Assert.DoesNotContain("confirm-email", emailSentByTheRegistrationService.Body);
    }

    // Tant que l'adresse n'est pas confirmée, une nouvelle inscription remplace le mot de passe et le lien (GR Auth).
    [Fact]
    public async Task UnconfirmedAddressReceivesNewLinkAndItsPasswordIsReplaced()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();
        var existingAccountNotYetConfirmedInThisTest = AddExistingAccountToTheFakeDatabase(emailAddressOfTheAccountIsAlreadyConfirmed: false);

        var resultOfTheVisitorAccountRegistration = await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        Assert.True(resultOfTheVisitorAccountRegistration.IsAccepted);
        Assert.Single(repositoryThatKeepsAccountsInMemory.SavedUsers);
        Assert.Equal(FakePasswordHasher.PrefixOfTheFakePasswordHashes + ValidPasswordChosenInTheseTests, existingAccountNotYetConfirmedInThisTest.PasswordHash);
        Assert.Empty(repositoryThatKeepsAccountsInMemory.SavedCategories);

        var tokenLeftAfterTheNewRegistration = Assert.Single(repositoryThatKeepsAccountsInMemory.SavedEmailTokens);
        Assert.Equal(FakeSecureTokenGenerator.HashAlwaysReturnedByThisFakeGenerator, tokenLeftAfterTheNewRegistration.TokenHash);
        Assert.Equal(existingAccountNotYetConfirmedInThisTest.Id, tokenLeftAfterTheNewRegistration.UserId);
        Assert.Equal(clockThatOnlyMovesWhenTestsAskIt.GetUtcNow().UtcDateTime.AddHours(24), tokenLeftAfterTheNewRegistration.ExpiresAt);

        var emailSentByTheRegistrationService = Assert.Single(senderThatKeepsTheEmailsInMemory.SentEmails);
        Assert.Contains("confirm-email#token=" + FakeSecureTokenGenerator.TokenAlwaysReturnedByThisFakeGenerator, emailSentByTheRegistrationService.Body);
    }

    // Le mot de passe est haché dans les trois cas : sinon, la durée de la réponse révélerait l'état de l'adresse.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingAddressStillHashesThePasswordToKeepTheSameDuration(bool emailAddressOfTheAccountIsAlreadyConfirmed)
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();
        AddExistingAccountToTheFakeDatabase(emailAddressOfTheAccountIsAlreadyConfirmed);

        await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        Assert.Equal(1, hasherThatCountsTheHashedPasswords.NumberOfPasswordsHashed);
    }

    // Deux inscriptions en même temps sur une adresse libre : la dernière remplace la première.
    [Fact]
    public async Task AddressTakenJustBeforeTheSaveByUnconfirmedAccountReceivesNewLink()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();
        var accountSavedByTheOtherRequest = new User { Email = EmailAddressInStoredFormInTheseTests, PasswordHash = OldSecureHashOfTheExistingAccount };
        repositoryThatKeepsAccountsInMemory.AccountSavedByAnotherRequestJustBeforeTheSave = accountSavedByTheOtherRequest;

        var resultOfTheVisitorAccountRegistration = await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        Assert.True(resultOfTheVisitorAccountRegistration.IsAccepted);
        Assert.Equal(FakePasswordHasher.PrefixOfTheFakePasswordHashes + ValidPasswordChosenInTheseTests, accountSavedByTheOtherRequest.PasswordHash);
        Assert.Contains("confirm-email", Assert.Single(senderThatKeepsTheEmailsInMemory.SentEmails).Body);
    }

    [Fact]
    public async Task AddressTakenJustBeforeTheSaveByConfirmedAccountWarnsItsOwner()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();
        var accountSavedByTheOtherRequest = new User
        {
            Email = EmailAddressInStoredFormInTheseTests,
            PasswordHash = OldSecureHashOfTheExistingAccount,
            IsEmailConfirmed = true
        };
        repositoryThatKeepsAccountsInMemory.AccountSavedByAnotherRequestJustBeforeTheSave = accountSavedByTheOtherRequest;

        var resultOfTheVisitorAccountRegistration = await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        Assert.True(resultOfTheVisitorAccountRegistration.IsAccepted);
        Assert.Equal(OldSecureHashOfTheExistingAccount, accountSavedByTheOtherRequest.PasswordHash);
        Assert.DoesNotContain("confirm-email", Assert.Single(senderThatKeepsTheEmailsInMemory.SentEmails).Body);
    }

    // Si le compte est confirmé au même instant, son mot de passe ne change pas : le propriétaire est prévenu.
    [Fact]
    public async Task AccountConfirmedJustBeforeThePasswordChangeOnlyWarnsItsOwner()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();
        var existingAccountNotYetConfirmedInThisTest = AddExistingAccountToTheFakeDatabase(emailAddressOfTheAccountIsAlreadyConfirmed: false);
        repositoryThatKeepsAccountsInMemory.AccountIsConfirmedJustBeforeThePasswordChange = true;

        var resultOfTheVisitorAccountRegistration = await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        Assert.True(resultOfTheVisitorAccountRegistration.IsAccepted);
        Assert.Equal(OldSecureHashOfTheExistingAccount, existingAccountNotYetConfirmedInThisTest.PasswordHash);
        Assert.DoesNotContain("confirm-email", Assert.Single(senderThatKeepsTheEmailsInMemory.SentEmails).Body);
    }

    // Si l'e-mail ne part pas, le visiteur est prévenu et le compte reste non confirmé (GR Auth).
    [Fact]
    public async Task EmailFailureForNewAccountIsReportedAndTheAccountStaysUnconfirmed()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();
        senderThatKeepsTheEmailsInMemory.EveryEmailFailsDuringThisTest = true;

        var resultOfTheVisitorAccountRegistration = await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        Assert.False(resultOfTheVisitorAccountRegistration.IsAccepted);
        Assert.True(resultOfTheVisitorAccountRegistration.EmailCouldNotBeSent);
        var userSavedByTheRegistrationService = Assert.Single(repositoryThatKeepsAccountsInMemory.SavedUsers);
        Assert.False(userSavedByTheRegistrationService.IsEmailConfirmed);
        Assert.Equal(userSavedByTheRegistrationService.Id, resultOfTheVisitorAccountRegistration.UserIdentifierForTheTechnicalLog);
    }

    // Une panne touche aussi l'e-mail de la nouvelle inscription et l'avertissement : rien ne distingue les adresses (SS §2).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmailFailureForExistingAddressIsReportedTheSameWay(bool emailAddressOfTheAccountIsAlreadyConfirmed)
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();
        var existingAccountAlreadyUsingTheAddress = AddExistingAccountToTheFakeDatabase(emailAddressOfTheAccountIsAlreadyConfirmed);
        senderThatKeepsTheEmailsInMemory.EveryEmailFailsDuringThisTest = true;

        var resultOfTheVisitorAccountRegistration = await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        Assert.True(resultOfTheVisitorAccountRegistration.EmailCouldNotBeSent);
        Assert.Equal(existingAccountAlreadyUsingTheAddress.Id, resultOfTheVisitorAccountRegistration.UserIdentifierForTheTechnicalLog);
    }

    // Après une panne de l'e-mail, une nouvelle inscription envoie un lien : le compte n'est jamais bloqué (GR Auth).
    [Fact]
    public async Task NewRegistrationAfterEmailFailureSendsNewLink()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();
        senderThatKeepsTheEmailsInMemory.EveryEmailFailsDuringThisTest = true;
        await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        senderThatKeepsTheEmailsInMemory.EveryEmailFailsDuringThisTest = false;
        var resultOfTheSecondVisitorRegistration = await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        Assert.True(resultOfTheSecondVisitorRegistration.IsAccepted);
        Assert.Contains("confirm-email", Assert.Single(senderThatKeepsTheEmailsInMemory.SentEmails).Body);
    }

    // Au-delà de la limite par adresse, rien n'est haché, enregistré ni envoyé (SS §6).
    [Fact]
    public async Task RequestOverTheLimitOfTheAddressIsRefusedWithoutDoingAnything()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();
        limiterThatCountsTheEmailAddresses.EveryRequestIsRefusedDuringThisTest = true;

        var resultOfTheVisitorAccountRegistration = await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        Assert.False(resultOfTheVisitorAccountRegistration.IsAccepted);
        Assert.True(resultOfTheVisitorAccountRegistration.TooManyRequestsForThisEmailAddress);
        Assert.Equal(0, hasherThatCountsTheHashedPasswords.NumberOfPasswordsHashed);
        Assert.Empty(repositoryThatKeepsAccountsInMemory.SavedUsers);
        Assert.Empty(senderThatKeepsTheEmailsInMemory.SentEmails);
    }

    // La limite compte la forme enregistrée : changer les majuscules ne la contourne pas.
    [Fact]
    public async Task LimitOfTheAddressCountsItsStoredForm()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();

        await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(
            EmailAddressTypedByTheVisitorInTheseTests, ValidPasswordChosenInTheseTests);

        Assert.Equal(EmailAddressInStoredFormInTheseTests, Assert.Single(limiterThatCountsTheEmailAddresses.EmailAddressesCounted));
    }

    // Une demande refusée par les règles n'envoie aucun e-mail : elle ne compte pas dans la limite de l'adresse.
    [Fact]
    public async Task RequestBreakingTheRulesIsNotCountedInTheLimitOfTheAddress()
    {
        var serviceBeingTestedWithFakeTools = CreateRegistrationServiceWithFakeTools();

        await serviceBeingTestedWithFakeTools.RegisterNewAccountForVisitorAsync(EmailAddressTypedByTheVisitorInTheseTests, "trop-court");

        Assert.Empty(limiterThatCountsTheEmailAddresses.EmailAddressesCounted);
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
