using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles de la double authentification (GR Auth, SS §2 et §6).
public class TwoFactorAuthenticationRulesTests
{
    [Fact]
    public void RulesHaveTheValuesWrittenInTheDocuments()
    {
        Assert.Equal(10, TwoFactorAuthenticationRules.NumberOfBackupCodesGivenAtEachActivation);
        Assert.Equal(5, TwoFactorAuthenticationRules.MaximumNumberOfCodesTriedDuringOneLogin);
        Assert.Equal(6, TwoFactorAuthenticationRules.NumberOfDigitsInOneAuthenticatorCode);
        Assert.Equal(TimeSpan.FromMinutes(5), TwoFactorAuthenticationRules.MaximumDurationOfLoginWaitingForItsCode);
        Assert.Equal(TimeSpan.FromMinutes(10), TwoFactorAuthenticationRules.MaximumDurationOfSecretWaitingForActivation);
    }

    // 32 caractères différents, soit 5 bits par caractère : 50 bits aléatoires pour un code entier.
    [Fact]
    public void BackupCodeAlphabetGivesFiftyRandomBitsWithoutConfusingCharacters()
    {
        Assert.Equal(32, TwoFactorAuthenticationRules.CharactersAllowedInOneBackupCode.Distinct().Count());
        Assert.Equal(10, TwoFactorAuthenticationRules.NumberOfCharactersInOneBackupCode);
        Assert.DoesNotContain('I', TwoFactorAuthenticationRules.CharactersAllowedInOneBackupCode);
        Assert.DoesNotContain('L', TwoFactorAuthenticationRules.CharactersAllowedInOneBackupCode);
        Assert.DoesNotContain('O', TwoFactorAuthenticationRules.CharactersAllowedInOneBackupCode);
        Assert.DoesNotContain('U', TwoFactorAuthenticationRules.CharactersAllowedInOneBackupCode);
    }

    [Theory]
    [InlineData("123456", "123456")]
    [InlineData("123 456", "123456")]
    [InlineData(" 123 456 ", "123456")]
    [InlineData("abcde-fghjk", "ABCDEFGHJK")]
    [InlineData("ABCDE-FGHJK", "ABCDEFGHJK")]
    [InlineData("ab cd-ef gh jk", "ABCDEFGHJK")]
    [InlineData("", "")]
    [InlineData("- -", "")]
    public void CodeTypedByTheUserIsWrittenInItsCleanForm(string codeTypedByTheUserInThisTest, string cleanFormExpectedByThisTest)
    {
        Assert.Equal(cleanFormExpectedByThisTest, TwoFactorAuthenticationRules.ConvertCodeTypedByTheUserIntoItsCleanForm(codeTypedByTheUserInThisTest));
    }

    [Theory]
    [InlineData("000000", true)]
    [InlineData("123456", true)]
    [InlineData("12345", false)]
    [InlineData("1234567", false)]
    [InlineData("", false)]
    [InlineData("12345A", false)]
    [InlineData("١٢٣٤٥٦", false)] // Chiffres arabes : char.IsDigit les accepterait.
    [InlineData("１２３４５６", false)] // Chiffres pleine largeur.
    [InlineData("12345\0", false)]
    public void AuthenticatorCodeFormatOnlyAcceptsSixAsciiDigits(string textToCheckInThisTest, bool answerExpectedFromTheRulesByThisTest)
    {
        Assert.Equal(answerExpectedFromTheRulesByThisTest, TwoFactorAuthenticationRules.CheckThatTextHasTheFormatOfAuthenticatorCode(textToCheckInThisTest));
    }

    [Theory]
    [InlineData("ABCDEFGHJK", true)]
    [InlineData("0123456789", true)]
    [InlineData("VWXYZ01234", true)]
    [InlineData("ABCDEFGHJ", false)]
    [InlineData("ABCDEFGHJKM", false)]
    [InlineData("", false)]
    [InlineData("ABCDEFGHJI", false)]
    [InlineData("ABCDEFGHJL", false)]
    [InlineData("ABCDEFGHJO", false)]
    [InlineData("ABCDEFGHJU", false)]
    [InlineData("abcdefghjk", false)]
    [InlineData("ABCDE-FGHJ", false)]
    [InlineData("ABCDEFGHJ\0", false)]
    [InlineData("ÀBCDEFGHJK", false)]
    public void BackupCodeFormatOnlyAcceptsTenCharactersOfTheAlphabet(string textToCheckInThisTest, bool answerExpectedFromTheRulesByThisTest)
    {
        Assert.Equal(answerExpectedFromTheRulesByThisTest, TwoFactorAuthenticationRules.CheckThatTextHasTheFormatOfOneBackupCode(textToCheckInThisTest));
    }

    [Fact]
    public void BackupCodeIsDisplayedWithOneDashAndComesBackToItsCleanForm()
    {
        var backupCodeInItsDisplayedForm = TwoFactorAuthenticationRules.ConvertBackupCodeIntoItsDisplayedForm("ABCDE12345");

        Assert.Equal("ABCDE-12345", backupCodeInItsDisplayedForm);
        Assert.Equal("ABCDE12345", TwoFactorAuthenticationRules.ConvertCodeTypedByTheUserIntoItsCleanForm(backupCodeInItsDisplayedForm));
    }
}
