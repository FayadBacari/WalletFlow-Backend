using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Application.TwoFactorServicesWithFakeTools;

namespace WalletFlow.Tests.Application;

// Vérifie l'activation de la double authentification, en deux temps (GR Auth, SS §2 et §11).
public class TwoFactorActivationServiceTests
{
    private readonly TwoFactorServicesWithFakeTools servicesWithFakeToolsOfTheseTests = new TwoFactorServicesWithFakeTools();

    // Commence l'activation de ce compte avec son bon mot de passe, et renvoie le secret.
    private async Task<string> StartActivationThenGetTheSecretAsync(User accountBeingActivatedInThisTest)
    {
        var resultOfTheStartOfTheActivation = await servicesWithFakeToolsOfTheseTests.CreateTwoFactorActivationServiceWithFakeTools().StartActivationWithThePasswordOfTheUserAsync(accountBeingActivatedInThisTest.Id, PasswordOfTheAccountInTheseTests, IpAddressOfTheVisitorInTheseTests);

        return resultOfTheStartOfTheActivation.SecretOfTheAuthenticatorApplication!;
    }

    // Finit l'activation de ce compte avec ce code.
    private async Task<TwoFactorActivationFinishResult> FinishActivationWithThisCodeAsync(User accountBeingActivatedInThisTest, string codeTypedByTheUserInThisTest)
    {
        return await servicesWithFakeToolsOfTheseTests.CreateTwoFactorActivationServiceWithFakeTools().FinishActivationWithTheFirstCodeOfTheUserAsync(accountBeingActivatedInThisTest.Id, codeTypedByTheUserInThisTest, IpAddressOfTheVisitorInTheseTests);
    }

    // --- Le premier temps : le mot de passe donne le secret

    // Tant que le premier code n'est pas donné, rien n'est écrit : un code QR mal scanné n'enferme personne dehors.
    [Fact]
    public async Task RightPasswordGivesTheSecretWithoutChangingTheAccount()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();

        var resultOfTheStartOfTheActivation = await servicesWithFakeToolsOfTheseTests.CreateTwoFactorActivationServiceWithFakeTools().StartActivationWithThePasswordOfTheUserAsync(accountUsedByThisTwoFactorTest.Id, PasswordOfTheAccountInTheseTests, IpAddressOfTheVisitorInTheseTests);

        Assert.True(resultOfTheStartOfTheActivation.IsActivationStarted);
        Assert.Matches("^[A-Z2-7]{32}$", resultOfTheStartOfTheActivation.SecretOfTheAuthenticatorApplication);
        Assert.Contains("secret=" + resultOfTheStartOfTheActivation.SecretOfTheAuthenticatorApplication, resultOfTheStartOfTheActivation.LinkThatAddsTheSecretToTheApplication);
        Assert.Contains("utilisateur%40exemple.test", resultOfTheStartOfTheActivation.LinkThatAddsTheSecretToTheApplication);
        Assert.False(accountUsedByThisTwoFactorTest.IsTwoFactorEnabled);
        Assert.Null(accountUsedByThisTwoFactorTest.EncryptedTotpSecret);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
    }

    [Fact]
    public async Task WrongPasswordGivesNoSecretAndIsWrittenInTheJournal()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();

        var resultOfTheStartOfTheActivation = await servicesWithFakeToolsOfTheseTests.CreateTwoFactorActivationServiceWithFakeTools().StartActivationWithThePasswordOfTheUserAsync(accountUsedByThisTwoFactorTest.Id, WrongPasswordTypedInTheseTests, IpAddressOfTheVisitorInTheseTests);

        Assert.False(resultOfTheStartOfTheActivation.IsActivationStarted);
        Assert.Null(resultOfTheStartOfTheActivation.SecretOfTheAuthenticatorApplication);
        Assert.Null(resultOfTheStartOfTheActivation.LinkThatAddsTheSecretToTheApplication);
        Assert.Equal(TwoFactorActivationService.ErrorMessageForWrongPasswordAtActivation, resultOfTheStartOfTheActivation.WrongPasswordErrorMessage);
        Assert.Null(servicesWithFakeToolsOfTheseTests.StoreOfTheSecretsWaitingForActivation.FindSecretWaitingForActivationOfThisAccount(accountUsedByThisTwoFactorTest.Id));

        var securityEventOfTheRefusalOfTheRequest = Assert.Single(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.TwoFactorActivation, securityEventOfTheRefusalOfTheRequest.Type);
        Assert.Equal(SecurityEventResult.Failure, securityEventOfTheRefusalOfTheRequest.Result);
        Assert.Equal(IpAddressOfTheVisitorInTheseTests, securityEventOfTheRefusalOfTheRequest.IpAddress);
        Assert.Equal(accountUsedByThisTwoFactorTest.Id, securityEventOfTheRefusalOfTheRequest.UserId);
    }

    // Ces essais ne comptent pas pour le blocage : un voleur de session pourrait sinon bloquer le compte de son propriétaire.
    [Fact]
    public async Task WrongPasswordDoesNotCountAsALoginAttemptOfTheAccount()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();

        await servicesWithFakeToolsOfTheseTests.CreateTwoFactorActivationServiceWithFakeTools().StartActivationWithThePasswordOfTheUserAsync(accountUsedByThisTwoFactorTest.Id, WrongPasswordTypedInTheseTests, IpAddressOfTheVisitorInTheseTests);

        Assert.Equal(0, accountUsedByThisTwoFactorTest.FailedLoginAttemptCount);
    }

    // Le même mot de passe, tapé sur un autre appareil, peut arriver écrit autrement (SS §2).
    [Fact]
    public async Task PasswordTypedWithDecomposedAccentIsAccepted()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        accountUsedByThisTwoFactorTest.PasswordHash = FakePasswordHasher.PrefixOfTheFakePasswordHashes + "Mot-De-Passé-1";

        var resultOfTheStartOfTheActivation = await servicesWithFakeToolsOfTheseTests.CreateTwoFactorActivationServiceWithFakeTools().StartActivationWithThePasswordOfTheUserAsync(accountUsedByThisTwoFactorTest.Id, "Mot-De-Passé-1", IpAddressOfTheVisitorInTheseTests);

        Assert.True(resultOfTheStartOfTheActivation.IsActivationStarted);
    }

    [Fact]
    public async Task AccountAlreadyActivatedIsRefusedWithoutCheckingThePassword()
    {
        var activatedAccountUsedByThisTest = servicesWithFakeToolsOfTheseTests.AddActivatedAccountToTheFakeDatabase();
        var encryptedSecretReadBeforeTheActivationAttempt = activatedAccountUsedByThisTest.UserAccount.EncryptedTotpSecret;

        var resultOfTheStartOfTheActivation = await servicesWithFakeToolsOfTheseTests.CreateTwoFactorActivationServiceWithFakeTools().StartActivationWithThePasswordOfTheUserAsync(activatedAccountUsedByThisTest.UserAccount.Id, PasswordOfTheAccountInTheseTests, IpAddressOfTheVisitorInTheseTests);

        Assert.Equal(TwoFactorActivationService.ErrorMessageForTwoFactorAlreadyEnabled, resultOfTheStartOfTheActivation.AlreadyEnabledErrorMessage);
        Assert.Null(resultOfTheStartOfTheActivation.SecretOfTheAuthenticatorApplication);
        Assert.Equal(0, servicesWithFakeToolsOfTheseTests.HasherThatCountsTheComparedPasswords.NumberOfPasswordsCompared);
        Assert.Equal(encryptedSecretReadBeforeTheActivationAttempt, activatedAccountUsedByThisTest.UserAccount.EncryptedTotpSecret);
    }

    [Fact]
    public async Task AccountEffacedJustBeforeTheStartGetsNoSecret()
    {
        var resultOfTheStartOfTheActivation = await servicesWithFakeToolsOfTheseTests.CreateTwoFactorActivationServiceWithFakeTools().StartActivationWithThePasswordOfTheUserAsync(Guid.NewGuid(), PasswordOfTheAccountInTheseTests, IpAddressOfTheVisitorInTheseTests);

        Assert.Equal(TwoFactorActivationService.ErrorMessageForWrongPasswordAtActivation, resultOfTheStartOfTheActivation.WrongPasswordErrorMessage);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
    }

    [Fact]
    public async Task StartingAgainReplacesTheSecretAndTheOldOneNoLongerWorks()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        var firstSecretOfTheAuthenticatorApplication = await StartActivationThenGetTheSecretAsync(accountUsedByThisTwoFactorTest);
        var secondSecretOfTheAuthenticatorApplication = await StartActivationThenGetTheSecretAsync(accountUsedByThisTwoFactorTest);

        Assert.NotEqual(firstSecretOfTheAuthenticatorApplication, secondSecretOfTheAuthenticatorApplication);

        var resultOfTheFinishWithTheOldSecret = await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(firstSecretOfTheAuthenticatorApplication));
        Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, resultOfTheFinishWithTheOldSecret.WrongCodeErrorMessage);

        var resultOfTheFinishWithTheNewSecret = await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(secondSecretOfTheAuthenticatorApplication));
        Assert.True(resultOfTheFinishWithTheNewSecret.IsTwoFactorActivated);
    }

    // --- Le second temps : le premier code active tout

    [Fact]
    public async Task RightFirstCodeActivatesEverythingAndGivesTenDifferentBackupCodes()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        var secretSharedWithTheAuthenticatorApplication = await StartActivationThenGetTheSecretAsync(accountUsedByThisTwoFactorTest);

        var resultOfTheEndOfTheActivation = await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(secretSharedWithTheAuthenticatorApplication));

        Assert.True(resultOfTheEndOfTheActivation.IsTwoFactorActivated);
        Assert.True(accountUsedByThisTwoFactorTest.IsTwoFactorEnabled);

        Assert.Equal(10, resultOfTheEndOfTheActivation.BackupCodesToGiveOnlyOnce!.Count);
        Assert.Equal(10, resultOfTheEndOfTheActivation.BackupCodesToGiveOnlyOnce.Distinct().Count());
        foreach (var backupCodeGivenToTheUser in resultOfTheEndOfTheActivation.BackupCodesToGiveOnlyOnce)
        {
            Assert.Matches("^[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}$", backupCodeGivenToTheUser);
        }

        var securityEventOfTheTwoFactorActivation = Assert.Single(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSecurityEventsInMemory.SavedSecurityEvents);
        Assert.Equal(SecurityEventType.TwoFactorActivation, securityEventOfTheTwoFactorActivation.Type);
        Assert.Equal(SecurityEventResult.Success, securityEventOfTheTwoFactorActivation.Result);
        Assert.Equal(IpAddressOfTheVisitorInTheseTests, securityEventOfTheTwoFactorActivation.IpAddress);
        Assert.Equal(accountUsedByThisTwoFactorTest.Id, securityEventOfTheTwoFactorActivation.UserId);
    }

    // SS §2 : le secret est chiffré, et il se relit avec la clé et l'identifiant du compte.
    [Fact]
    public async Task SecretIsSavedEncryptedAndCanBeReadBackWithTheKey()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        var secretSharedWithTheAuthenticatorApplication = await StartActivationThenGetTheSecretAsync(accountUsedByThisTwoFactorTest);

        await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(secretSharedWithTheAuthenticatorApplication));

        Assert.DoesNotContain(secretSharedWithTheAuthenticatorApplication, accountUsedByThisTwoFactorTest.EncryptedTotpSecret);
        Assert.Equal(secretSharedWithTheAuthenticatorApplication, servicesWithFakeToolsOfTheseTests.RealProtectorOfTheSecrets.DecryptSecretOfThisUserAccount(accountUsedByThisTwoFactorTest.EncryptedTotpSecret!, accountUsedByThisTwoFactorTest.Id));
        Assert.True(accountUsedByThisTwoFactorTest.EncryptedTotpSecret!.Length <= 255);
    }

    // SS §2 : les codes de secours sont stockés hachés, chacun correspond à ce qui a été donné, et aucun n'a encore servi.
    [Fact]
    public async Task BackupCodesAreSavedHashedForThisAccountAndNotYetUsed()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        var secretSharedWithTheAuthenticatorApplication = await StartActivationThenGetTheSecretAsync(accountUsedByThisTwoFactorTest);

        var resultOfTheEndOfTheActivation = await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(secretSharedWithTheAuthenticatorApplication));

        var backupCodesSavedInTheFakeDatabase = servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes;
        Assert.Equal(10, backupCodesSavedInTheFakeDatabase.Count);

        for (var positionOfTheBackupCodeBeingChecked = 0; positionOfTheBackupCodeBeingChecked < 10; positionOfTheBackupCodeBeingChecked++)
        {
            var cleanFormOfTheBackupCodeGiven = TwoFactorAuthenticationRules.ConvertCodeTypedByTheUserIntoItsCleanForm(resultOfTheEndOfTheActivation.BackupCodesToGiveOnlyOnce![positionOfTheBackupCodeBeingChecked]);
            Assert.Equal(FakePasswordHasher.PrefixOfTheFakePasswordHashes + cleanFormOfTheBackupCodeGiven, backupCodesSavedInTheFakeDatabase[positionOfTheBackupCodeBeingChecked].CodeHash);
            Assert.Equal(accountUsedByThisTwoFactorTest.Id, backupCodesSavedInTheFakeDatabase[positionOfTheBackupCodeBeingChecked].UserId);
            Assert.Null(backupCodesSavedInTheFakeDatabase[positionOfTheBackupCodeBeingChecked].UsedAt);
        }
    }

    [Fact]
    public async Task SecretWaitingInMemoryIsForgottenOnceActivated()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        var secretSharedWithTheAuthenticatorApplication = await StartActivationThenGetTheSecretAsync(accountUsedByThisTwoFactorTest);

        await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(secretSharedWithTheAuthenticatorApplication));

        Assert.Null(servicesWithFakeToolsOfTheseTests.StoreOfTheSecretsWaitingForActivation.FindSecretWaitingForActivationOfThisAccount(accountUsedByThisTwoFactorTest.Id));
    }

    [Fact]
    public async Task WrongFirstCodeActivatesNothingAndCanBeRetried()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        var secretSharedWithTheAuthenticatorApplication = await StartActivationThenGetTheSecretAsync(accountUsedByThisTwoFactorTest);

        var resultOfTheWrongCodeOfTheVisitor = await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, servicesWithFakeToolsOfTheseTests.FindCodeThatIsWrongNow(secretSharedWithTheAuthenticatorApplication));

        Assert.False(resultOfTheWrongCodeOfTheVisitor.IsTwoFactorActivated);
        Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, resultOfTheWrongCodeOfTheVisitor.WrongCodeErrorMessage);
        Assert.False(accountUsedByThisTwoFactorTest.IsTwoFactorEnabled);
        Assert.Null(accountUsedByThisTwoFactorTest.EncryptedTotpSecret);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes);
        Assert.Equal(1, servicesWithFakeToolsOfTheseTests.CountSecurityEventsInTheJournal(SecurityEventType.TwoFactorActivation, SecurityEventResult.Failure));

        var resultOfTheRightCodeOfTheVisitor = await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(secretSharedWithTheAuthenticatorApplication));
        Assert.True(resultOfTheRightCodeOfTheVisitor.IsTwoFactorActivated);
    }

    // Le compte n'a pas encore de code de secours : un texte de cette forme ne doit lancer aucun Argon2.
    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("ABCDE-FGHJK")]
    [InlineData("١٢٣٤٥٦")]
    public async Task TextThatIsNotAnAuthenticatorCodeIsRefusedWithoutAnyHash(string textTypedByTheUserInThisTest)
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        await StartActivationThenGetTheSecretAsync(accountUsedByThisTwoFactorTest);
        var numberOfComparisonsBeforeTheCode = servicesWithFakeToolsOfTheseTests.HasherThatCountsTheComparedPasswords.NumberOfPasswordsCompared;

        var resultOfTheEndOfTheActivation = await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, textTypedByTheUserInThisTest);

        Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, resultOfTheEndOfTheActivation.WrongCodeErrorMessage);
        Assert.Equal(numberOfComparisonsBeforeTheCode, servicesWithFakeToolsOfTheseTests.HasherThatCountsTheComparedPasswords.NumberOfPasswordsCompared);
        Assert.Equal(0, servicesWithFakeToolsOfTheseTests.HasherThatCountsTheComparedPasswords.NumberOfPasswordsHashed);
    }

    // SS §2 : le premier code a déjà servi. Il ne peut pas servir à se connecter ni à désactiver juste après.
    [Fact]
    public async Task FirstCodeCannotBeUsedAgainToLogIn()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        var secretSharedWithTheAuthenticatorApplication = await StartActivationThenGetTheSecretAsync(accountUsedByThisTwoFactorTest);
        var firstCodeTypedByTheUserAtActivation = servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(secretSharedWithTheAuthenticatorApplication);
        await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, firstCodeTypedByTheUserAtActivation);

        var waitingLoginTokenOfTheVisitor = await servicesWithFakeToolsOfTheseTests.LogInWithTheRightPasswordThenGetTheWaitingLoginTokenAsync();
        var resultOfTheLoginWithTheFirstCode = await servicesWithFakeToolsOfTheseTests.FinishLoginWithThisCodeAsync(waitingLoginTokenOfTheVisitor, firstCodeTypedByTheUserAtActivation);

        Assert.Equal(TwoFactorCodeCheckingService.ErrorMessageForWrongTwoFactorCode, resultOfTheLoginWithTheFirstCode.WrongCodeErrorMessage);
    }

    [Fact]
    public async Task FinishingWithoutAnyStartAsksToRestart()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();

        var resultOfTheEndOfTheActivation = await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, "123456");

        Assert.Equal(TwoFactorActivationService.ErrorMessageForActivationThatMustRestart, resultOfTheEndOfTheActivation.ActivationMustRestartErrorMessage);
        Assert.False(accountUsedByThisTwoFactorTest.IsTwoFactorEnabled);
    }

    [Fact]
    public async Task SecretWaitingForTheFirstCodeStillWorksJustBeforeTheEndOfTheTenMinutes()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        var secretSharedWithTheAuthenticatorApplication = await StartActivationThenGetTheSecretAsync(accountUsedByThisTwoFactorTest);

        servicesWithFakeToolsOfTheseTests.ClockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        var resultOfTheEndOfTheActivation = await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(secretSharedWithTheAuthenticatorApplication));

        Assert.True(resultOfTheEndOfTheActivation.IsTwoFactorActivated);
    }

    [Fact]
    public async Task SecretWaitingForTheFirstCodeEndsExactlyAfterTenMinutes()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        var secretSharedWithTheAuthenticatorApplication = await StartActivationThenGetTheSecretAsync(accountUsedByThisTwoFactorTest);

        servicesWithFakeToolsOfTheseTests.ClockThatOnlyMovesWhenTestsAskIt.MoveFakeClockForwardByThisDuration(TimeSpan.FromMinutes(10));
        var resultOfTheEndOfTheActivation = await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(secretSharedWithTheAuthenticatorApplication));

        Assert.Equal(TwoFactorActivationService.ErrorMessageForActivationThatMustRestart, resultOfTheEndOfTheActivation.ActivationMustRestartErrorMessage);
        Assert.False(accountUsedByThisTwoFactorTest.IsTwoFactorEnabled);
    }

    // SS §4 : le secret en attente n'appartient qu'au compte qui l'a demandé.
    [Fact]
    public async Task SecretWaitingForOneAccountCannotActivateAnotherAccount()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        var otherAccountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase("autre-compte@exemple.test");
        var secretSharedWithTheAuthenticatorApplication = await StartActivationThenGetTheSecretAsync(accountUsedByThisTwoFactorTest);

        var resultOfTheFinishForTheOtherAccount = await FinishActivationWithThisCodeAsync(otherAccountUsedByThisTwoFactorTest, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(secretSharedWithTheAuthenticatorApplication));

        Assert.Equal(TwoFactorActivationService.ErrorMessageForActivationThatMustRestart, resultOfTheFinishForTheOtherAccount.ActivationMustRestartErrorMessage);
        Assert.False(otherAccountUsedByThisTwoFactorTest.IsTwoFactorEnabled);
    }

    // --- Les conflits et les pannes

    [Fact]
    public async Task ConflictsWithOtherRequestsAreRetried()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        var secretSharedWithTheAuthenticatorApplication = await StartActivationThenGetTheSecretAsync(accountUsedByThisTwoFactorTest);
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.NumberOfConflictsBeforeTheActivationIsSaved = 9;

        var resultOfTheEndOfTheActivation = await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(secretSharedWithTheAuthenticatorApplication));

        Assert.True(resultOfTheEndOfTheActivation.IsTwoFactorActivated);
        Assert.Equal(10, servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes.Count);
        Assert.Equal(1, servicesWithFakeToolsOfTheseTests.CountSecurityEventsInTheJournal(SecurityEventType.TwoFactorActivation, SecurityEventResult.Success));
    }

    [Fact]
    public async Task TooManyConflictsAskToRestartAndActivateNothing()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        var secretSharedWithTheAuthenticatorApplication = await StartActivationThenGetTheSecretAsync(accountUsedByThisTwoFactorTest);
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.NumberOfConflictsBeforeTheActivationIsSaved = 10;

        var resultOfTheEndOfTheActivation = await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(secretSharedWithTheAuthenticatorApplication));

        Assert.Equal(TwoFactorActivationService.ErrorMessageForActivationThatMustRestart, resultOfTheEndOfTheActivation.ActivationMustRestartErrorMessage);
        Assert.False(accountUsedByThisTwoFactorTest.IsTwoFactorEnabled);
        Assert.Null(resultOfTheEndOfTheActivation.BackupCodesToGiveOnlyOnce);
    }

    // Deux onglets : l'activation de l'autre a réussi juste avant. Ce compte ne reçoit jamais 20 codes de secours.
    [Fact]
    public async Task AccountActivatedByAnotherRequestJustBeforeTheSaveKeepsItsFirstActivation()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        var secretSharedWithTheAuthenticatorApplication = await StartActivationThenGetTheSecretAsync(accountUsedByThisTwoFactorTest);
        accountUsedByThisTwoFactorTest.IsTwoFactorEnabled = true;
        accountUsedByThisTwoFactorTest.EncryptedTotpSecret = "secret-de-l-autre-onglet";

        var resultOfTheEndOfTheActivation = await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(secretSharedWithTheAuthenticatorApplication));

        Assert.Equal(TwoFactorActivationService.ErrorMessageForActivationThatMustRestart, resultOfTheEndOfTheActivation.ActivationMustRestartErrorMessage);
        Assert.Equal("secret-de-l-autre-onglet", accountUsedByThisTwoFactorTest.EncryptedTotpSecret);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes);
    }

    [Fact]
    public async Task AccountEffacedJustBeforeTheSaveActivatesNothing()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        var secretSharedWithTheAuthenticatorApplication = await StartActivationThenGetTheSecretAsync(accountUsedByThisTwoFactorTest);
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsAccountsInMemory.SavedUsers.Remove(accountUsedByThisTwoFactorTest);

        var resultOfTheEndOfTheActivation = await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(secretSharedWithTheAuthenticatorApplication));

        Assert.Equal(TwoFactorActivationService.ErrorMessageForActivationThatMustRestart, resultOfTheEndOfTheActivation.ActivationMustRestartErrorMessage);
        Assert.Empty(servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.SavedBackupCodes);
    }

    // Une panne n'active rien et ne perd pas le secret : le code suivant du téléphone fonctionne, sans rescanner le code QR.
    [Fact]
    public async Task DatabaseFailureDuringTheSaveIsNotHiddenAndTheNextCodeWorksAfterIt()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        var secretSharedWithTheAuthenticatorApplication = await StartActivationThenGetTheSecretAsync(accountUsedByThisTwoFactorTest);
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.DatabaseIsDownWhileSavingTheSettings = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(secretSharedWithTheAuthenticatorApplication)));
        Assert.False(accountUsedByThisTwoFactorTest.IsTwoFactorEnabled);

        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsTwoFactorDataInMemory.DatabaseIsDownWhileSavingTheSettings = false;
        servicesWithFakeToolsOfTheseTests.WaitForTheNextCodeOnThePhone();
        var resultOfTheCodeAfterTheFailure = await FinishActivationWithThisCodeAsync(accountUsedByThisTwoFactorTest, servicesWithFakeToolsOfTheseTests.CalculateCodeShownOnThePhoneNow(secretSharedWithTheAuthenticatorApplication));

        Assert.True(resultOfTheCodeAfterTheFailure.IsTwoFactorActivated);
    }

    [Fact]
    public async Task JournalFailureOnAWrongPasswordIsNotHidden()
    {
        var accountUsedByThisTwoFactorTest = servicesWithFakeToolsOfTheseTests.AddAccountWithoutTwoFactorToTheFakeDatabase();
        servicesWithFakeToolsOfTheseTests.RepositoryThatKeepsSecurityEventsInMemory.DatabaseIsDownDuringThisTest = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicesWithFakeToolsOfTheseTests.CreateTwoFactorActivationServiceWithFakeTools().StartActivationWithThePasswordOfTheUserAsync(accountUsedByThisTwoFactorTest.Id, WrongPasswordTypedInTheseTests, IpAddressOfTheVisitorInTheseTests));
    }
}
