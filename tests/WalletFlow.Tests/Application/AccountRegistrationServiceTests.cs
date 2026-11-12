using WalletFlow.Application.Configuration;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Emails;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le cas d'utilisation de l'inscription (GR Auth, SS §2), avec de faux outils.
public class AccountRegistrationServiceTests
{
    private const string TypedEmail = " Nouvel.Utilisateur@Exemple.test ";
    private const string Email = "nouvel.utilisateur@exemple.test";
    private const string ValidPassword = "Mot-De-Passe-Valide-1";
    private const string OldHash = "ancienne-empreinte-du-compte";

    private readonly FakeUserAccountRepository accountRepository = new FakeUserAccountRepository();
    private readonly FakePasswordHasher passwordHasher = new FakePasswordHasher();
    private readonly FakeEmailSender emailSender = new FakeEmailSender();
    private readonly FakeEmailAddressRequestLimiter requestLimiter = new FakeEmailAddressRequestLimiter();
    private readonly FakeTimeProvider clock = new FakeTimeProvider();

    // Crée le service à tester, avec ses faux outils.
    private AccountRegistrationService CreateRegistrationService()
    {
        return new AccountRegistrationService(
            accountRepository,
            new FakeDatabaseUnitOfWork(accountRepository),
            passwordHasher,
            new FakeSecureTokenGenerator(),
            emailSender,
            new FrenchAccountRegistrationEmails(),
            new PublicWebsiteSettings("https://walletflow.test/"),
            requestLimiter,
            clock);
    }

    // Le mot de passe est normalisé avant le hachage : tapé sur un autre appareil, il donnera le même résultat (SS §2).
    [Fact]
    public async Task PasswordTypedWithDecomposedAccentIsHashedInItsNormalizedForm()
    {
        var service = CreateRegistrationService();

        await service.RegisterAsync(TypedEmail, "Mot-De-Passe\u0301-1");

        Assert.Equal(FakePasswordHasher.HashPrefix + "Mot-De-Passé-1", accountRepository.SavedUsers[0].PasswordHash);
    }

    // Le mot de passe est normalisé avant les règles : 12 signes sans normalisation, mais 11 vrais caractères (SS §2).
    [Fact]
    public async Task PasswordLengthIsCountedAfterTheNormalization()
    {
        var service = CreateRegistrationService();

        var result = await service.RegisterAsync(TypedEmail, "Abcdefgh-1e\u0301");

        var refusal = Assert.IsType<AccountRegistrationResult.RefusedByRules>(result);
        Assert.True(refusal.IsPasswordInvalid);
    }

    // Ajoute à la fausse base un compte qui utilise déjà l'adresse de ces tests, avec son ancien jeton.
    private User AddExistingAccount(bool isEmailConfirmed)
    {
        var existingAccount = new User
        {
            Email = Email,
            PasswordHash = OldHash,
            IsEmailConfirmed = isEmailConfirmed
        };
        accountRepository.SavedUsers.Add(existingAccount);
        accountRepository.SavedEmailTokens.Add(new EmailToken
        {
            Type = EmailTokenType.SignupConfirmation,
            TokenHash = "ancienne-empreinte-du-jeton",
            UserId = existingAccount.Id
        });

        return existingAccount;
    }

    [Fact]
    public async Task NewAccountIsSavedWithItsStoredEmailAndItsHashedPassword()
    {
        var service = CreateRegistrationService();

        var result = await service.RegisterAsync(
            TypedEmail, ValidPassword);

        Assert.IsType<AccountRegistrationResult.Accepted>(result);
        var user = Assert.Single(accountRepository.SavedUsers);
        Assert.Equal(Email, user.Email);
        Assert.Equal(FakePasswordHasher.HashPrefix + ValidPassword, user.PasswordHash);
        Assert.Equal(UserRole.User, user.Role);
        Assert.False(user.IsEmailConfirmed);
    }

    [Fact]
    public async Task NewAccountReceivesItsEightCategoriesAndItsConfirmationToken()
    {
        var service = CreateRegistrationService();

        await service.RegisterAsync(
            TypedEmail, ValidPassword);

        var user = Assert.Single(accountRepository.SavedUsers);
        Assert.Equal(8, accountRepository.SavedCategories.Count);

        // Chaque catégorie appartient bien au nouvel utilisateur.
        foreach (var category in accountRepository.SavedCategories)
        {
            Assert.Equal(user.Id, category.UserId);
        }

        // Seule l'empreinte du jeton va en base, jamais le jeton lui-même (SS §2).
        var token = Assert.Single(accountRepository.SavedEmailTokens);
        Assert.Equal(EmailTokenType.SignupConfirmation, token.Type);
        Assert.Equal(FakeSecureTokenGenerator.FixedHash, token.TokenHash);
        Assert.Equal(user.Id, token.UserId);

        // Le lien de confirmation est valable exactement 24 heures.
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddHours(24), token.ExpiresAt);
    }

    [Fact]
    public async Task NewAccountReceivesConfirmationEmailContainingTheTokenLink()
    {
        var service = CreateRegistrationService();

        await service.RegisterAsync(
            TypedEmail, ValidPassword);

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(Email, email.RecipientAddress);
        Assert.Contains(
            "https://walletflow.test/confirm-email#token=" + FakeSecureTokenGenerator.FixedToken,
            email.Body);

        // L'e-mail dit qu'il faudra saisir le mot de passe, et annonce la vraie durée du lien (GR Auth, SS §2).
        Assert.Contains("saisis le mot de passe", email.Body);
        Assert.Contains("valable 24 heures", email.Body);

        // Un e-mail ne contient jamais de mot de passe (SS §9).
        Assert.DoesNotContain(ValidPassword, email.Body);
    }

    // Une adresse confirmée ne change jamais : son propriétaire est seulement prévenu (SS §2).
    [Fact]
    public async Task ConfirmedAddressChangesNothingAndWarnsItsOwner()
    {
        var service = CreateRegistrationService();
        var confirmedAccount = AddExistingAccount(isEmailConfirmed: true);

        var result = await service.RegisterAsync(
            TypedEmail, ValidPassword);

        // La réponse reste la même : seul le propriétaire de l'adresse apprend qu'elle est déjà utilisée.
        Assert.IsType<AccountRegistrationResult.Accepted>(result);
        Assert.Single(accountRepository.SavedUsers);
        Assert.Equal(OldHash, confirmedAccount.PasswordHash);
        Assert.Equal("ancienne-empreinte-du-jeton", Assert.Single(accountRepository.SavedEmailTokens).TokenHash);
        var email = Assert.Single(emailSender.SentEmails);
        Assert.Equal(Email, email.RecipientAddress);
        Assert.DoesNotContain("confirm-email", email.Body);
    }

    // Tant que l'adresse n'est pas confirmée, une nouvelle inscription remplace le mot de passe et le lien (GR Auth).
    [Fact]
    public async Task UnconfirmedAddressReceivesNewLinkAndItsPasswordIsReplaced()
    {
        var service = CreateRegistrationService();
        var unconfirmedAccount = AddExistingAccount(isEmailConfirmed: false);

        var result = await service.RegisterAsync(
            TypedEmail, ValidPassword);

        Assert.IsType<AccountRegistrationResult.Accepted>(result);
        Assert.Single(accountRepository.SavedUsers);
        Assert.Equal(FakePasswordHasher.HashPrefix + ValidPassword, unconfirmedAccount.PasswordHash);
        Assert.Empty(accountRepository.SavedCategories);

        var token = Assert.Single(accountRepository.SavedEmailTokens);
        Assert.Equal(FakeSecureTokenGenerator.FixedHash, token.TokenHash);
        Assert.Equal(unconfirmedAccount.Id, token.UserId);
        Assert.Equal(clock.GetUtcNow().UtcDateTime.AddHours(24), token.ExpiresAt);

        var email = Assert.Single(emailSender.SentEmails);
        Assert.Contains("confirm-email#token=" + FakeSecureTokenGenerator.FixedToken, email.Body);
    }

    // Le mot de passe est haché dans les trois cas : sinon, la durée de la réponse révélerait l'état de l'adresse.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingAddressStillHashesThePasswordToKeepTheSameDuration(bool isEmailConfirmed)
    {
        var service = CreateRegistrationService();
        AddExistingAccount(isEmailConfirmed);

        await service.RegisterAsync(
            TypedEmail, ValidPassword);

        Assert.Equal(1, passwordHasher.HashedPasswordCount);
    }

    // Deux inscriptions en même temps sur une adresse libre : la dernière remplace la première.
    [Fact]
    public async Task AddressTakenJustBeforeTheSaveByUnconfirmedAccountReceivesNewLink()
    {
        var service = CreateRegistrationService();
        var account = new User { Email = Email, PasswordHash = OldHash };
        accountRepository.ConcurrentAccount = account;

        var result = await service.RegisterAsync(
            TypedEmail, ValidPassword);

        Assert.IsType<AccountRegistrationResult.Accepted>(result);
        Assert.Equal(FakePasswordHasher.HashPrefix + ValidPassword, account.PasswordHash);
        Assert.Contains("confirm-email", Assert.Single(emailSender.SentEmails).Body);
    }

    [Fact]
    public async Task AddressTakenJustBeforeTheSaveByConfirmedAccountWarnsItsOwner()
    {
        var service = CreateRegistrationService();
        var account = new User
        {
            Email = Email,
            PasswordHash = OldHash,
            IsEmailConfirmed = true
        };
        accountRepository.ConcurrentAccount = account;

        var result = await service.RegisterAsync(
            TypedEmail, ValidPassword);

        Assert.IsType<AccountRegistrationResult.Accepted>(result);
        Assert.Equal(OldHash, account.PasswordHash);
        Assert.DoesNotContain("confirm-email", Assert.Single(emailSender.SentEmails).Body);
    }

    // Si le compte est confirmé au même instant, son mot de passe ne change pas : le propriétaire est prévenu.
    [Fact]
    public async Task AccountConfirmedJustBeforeThePasswordChangeOnlyWarnsItsOwner()
    {
        var service = CreateRegistrationService();
        var unconfirmedAccount = AddExistingAccount(isEmailConfirmed: false);
        accountRepository.ConfirmBeforePasswordChange = true;

        var result = await service.RegisterAsync(
            TypedEmail, ValidPassword);

        Assert.IsType<AccountRegistrationResult.Accepted>(result);
        Assert.Equal(OldHash, unconfirmedAccount.PasswordHash);
        Assert.DoesNotContain("confirm-email", Assert.Single(emailSender.SentEmails).Body);
    }

    // Si l'e-mail ne part pas, le visiteur est prévenu et le compte reste non confirmé (GR Auth).
    [Fact]
    public async Task EmailFailureForNewAccountIsReportedAndTheAccountStaysUnconfirmed()
    {
        var service = CreateRegistrationService();
        emailSender.AlwaysFails = true;

        var result = await service.RegisterAsync(
            TypedEmail, ValidPassword);

        var emailNotSent = Assert.IsType<AccountRegistrationResult.EmailCouldNotBeSent>(result);
        var user = Assert.Single(accountRepository.SavedUsers);
        Assert.False(user.IsEmailConfirmed);
        Assert.Equal(user.Id, emailNotSent.UserId);
    }

    // Une panne touche aussi l'e-mail de la nouvelle inscription et l'avertissement : rien ne distingue les adresses (SS §2).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmailFailureForExistingAddressIsReportedTheSameWay(bool isEmailConfirmed)
    {
        var service = CreateRegistrationService();
        var existingAccount = AddExistingAccount(isEmailConfirmed);
        emailSender.AlwaysFails = true;

        var result = await service.RegisterAsync(
            TypedEmail, ValidPassword);

        var emailNotSent = Assert.IsType<AccountRegistrationResult.EmailCouldNotBeSent>(result);
        Assert.Equal(existingAccount.Id, emailNotSent.UserId);
    }

    // Après une panne de l'e-mail, une nouvelle inscription envoie un lien : le compte n'est jamais bloqué (GR Auth).
    [Fact]
    public async Task NewRegistrationAfterEmailFailureSendsNewLink()
    {
        var service = CreateRegistrationService();
        emailSender.AlwaysFails = true;
        await service.RegisterAsync(TypedEmail, ValidPassword);

        emailSender.AlwaysFails = false;
        var result = await service.RegisterAsync(
            TypedEmail, ValidPassword);

        Assert.IsType<AccountRegistrationResult.Accepted>(result);
        Assert.Contains("confirm-email", Assert.Single(emailSender.SentEmails).Body);
    }

    // Au-delà de la limite par adresse, rien n'est haché, enregistré ni envoyé (SS §6).
    [Fact]
    public async Task RequestOverTheLimitOfTheAddressIsRefusedWithoutDoingAnything()
    {
        var service = CreateRegistrationService();
        requestLimiter.RefusesEveryRequest = true;

        var result = await service.RegisterAsync(
            TypedEmail, ValidPassword);

        Assert.IsType<AccountRegistrationResult.TooManyRequests>(result);
        Assert.Equal(0, passwordHasher.HashedPasswordCount);
        Assert.Empty(accountRepository.SavedUsers);
        Assert.Empty(emailSender.SentEmails);
    }

    // La limite compte la forme enregistrée : changer les majuscules ne la contourne pas.
    [Fact]
    public async Task LimitOfTheAddressCountsItsStoredForm()
    {
        var service = CreateRegistrationService();

        await service.RegisterAsync(
            TypedEmail, ValidPassword);

        Assert.Equal(Email, Assert.Single(requestLimiter.CountedAddresses));
    }

    // Une demande refusée par les règles n'envoie aucun e-mail : elle ne compte pas dans la limite de l'adresse.
    [Fact]
    public async Task RequestBreakingTheRulesIsNotCountedInTheLimitOfTheAddress()
    {
        var service = CreateRegistrationService();

        await service.RegisterAsync(TypedEmail, "trop-court");

        Assert.Empty(requestLimiter.CountedAddresses);
    }

    [Fact]
    public async Task InvalidEmailAddressIsRefusedWithoutSavingOrSendingAnything()
    {
        var service = CreateRegistrationService();

        var result = await service.RegisterAsync(
            "adresse-sans-arobase", ValidPassword);

        var refusal = Assert.IsType<AccountRegistrationResult.RefusedByRules>(result);
        Assert.True(refusal.IsEmailInvalid);
        Assert.False(refusal.IsPasswordInvalid);
        Assert.Empty(accountRepository.SavedUsers);
        Assert.Empty(emailSender.SentEmails);
    }

    [Fact]
    public async Task PasswordBreakingTheRulesIsRefusedWithoutHashingOrSavingAnything()
    {
        var service = CreateRegistrationService();

        var result = await service.RegisterAsync(
            TypedEmail, "trop-court");

        var refusal = Assert.IsType<AccountRegistrationResult.RefusedByRules>(result);
        Assert.True(refusal.IsPasswordInvalid);
        Assert.False(refusal.IsEmailInvalid);
        Assert.Equal(0, passwordHasher.HashedPasswordCount);
        Assert.Empty(accountRepository.SavedUsers);
        Assert.Empty(emailSender.SentEmails);
    }

    [Fact]
    public async Task BothErrorsAreReturnedTogetherWhenEmailAndPasswordAreInvalid()
    {
        var service = CreateRegistrationService();

        var result = await service.RegisterAsync(
            "adresse-sans-arobase", "trop-court");

        var refusal = Assert.IsType<AccountRegistrationResult.RefusedByRules>(result);
        Assert.True(refusal.IsEmailInvalid);
        Assert.True(refusal.IsPasswordInvalid);
    }
}
