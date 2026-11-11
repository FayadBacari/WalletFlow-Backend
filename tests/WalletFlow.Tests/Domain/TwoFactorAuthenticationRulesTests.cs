using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles de la double authentification (GR Auth, SS §2 et §6).
public class TwoFactorAuthenticationRulesTests
{
    [Fact]
    public void RulesHaveTheValuesWrittenInTheDocuments()
    {
        Assert.Equal(10, TwoFactorAuthenticationRules.BackupCodesPerActivation);
        Assert.Equal(5, TwoFactorAuthenticationRules.MaximumCodesPerLogin);
        Assert.Equal(6, TwoFactorAuthenticationRules.AuthenticatorCodeLength);
        Assert.Equal(TimeSpan.FromMinutes(5), TwoFactorAuthenticationRules.WaitingLoginLifetime);
        Assert.Equal(TimeSpan.FromMinutes(10), TwoFactorAuthenticationRules.WaitingSecretLifetime);
    }

    // 32 caractères différents, soit 5 bits par caractère : 50 bits aléatoires pour un code entier.
    [Fact]
    public void BackupCodeAlphabetGivesFiftyRandomBitsWithoutConfusingCharacters()
    {
        Assert.Equal(32, TwoFactorAuthenticationRules.BackupCodeAlphabet.Distinct().Count());
        Assert.Equal(10, TwoFactorAuthenticationRules.BackupCodeLength);
        Assert.DoesNotContain('I', TwoFactorAuthenticationRules.BackupCodeAlphabet);
        Assert.DoesNotContain('L', TwoFactorAuthenticationRules.BackupCodeAlphabet);
        Assert.DoesNotContain('O', TwoFactorAuthenticationRules.BackupCodeAlphabet);
        Assert.DoesNotContain('U', TwoFactorAuthenticationRules.BackupCodeAlphabet);
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
    public void CodeTypedByTheUserIsWrittenInItsCleanForm(string code, string expectedCleanForm)
    {
        Assert.Equal(expectedCleanForm, TwoFactorAuthenticationRules.CleanCode(code));
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
    public void AuthenticatorCodeFormatOnlyAcceptsSixAsciiDigits(string text, bool expected)
    {
        Assert.Equal(expected, TwoFactorAuthenticationRules.HasAuthenticatorCodeFormat(text));
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
    public void BackupCodeFormatOnlyAcceptsTenCharactersOfTheAlphabet(string text, bool expected)
    {
        Assert.Equal(expected, TwoFactorAuthenticationRules.HasBackupCodeFormat(text));
    }

    [Fact]
    public void BackupCodeIsDisplayedWithOneDashAndComesBackToItsCleanForm()
    {
        var displayedCode = TwoFactorAuthenticationRules.FormatBackupCode("ABCDE12345");

        Assert.Equal("ABCDE-12345", displayedCode);
        Assert.Equal("ABCDE12345", TwoFactorAuthenticationRules.CleanCode(displayedCode));
    }
}
