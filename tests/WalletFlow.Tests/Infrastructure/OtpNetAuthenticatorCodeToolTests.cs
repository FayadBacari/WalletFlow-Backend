using OtpNet;
using WalletFlow.Infrastructure.Security;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le calcul des codes (SS §2), avec les valeurs publiées par la RFC 6238 : une vraie application d'authentification calcule les mêmes.
public class OtpNetAuthenticatorCodeToolTests
{
    // Le secret de la RFC 6238 : « 12345678901234567890 », écrit en Base32.
    private const string RfcSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private static readonly DateTime CodeDate = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly OtpNetAuthenticatorCodeTool tool = new OtpNetAuthenticatorCodeTool();

    // Calcule le code qu'affiche un téléphone à cette date.
    private static string CalculateCodeAt(DateTime date)
    {
        return new Totp(Base32Encoding.ToBytes(RfcSecret)).ComputeTotp(date);
    }

    // Les 6 derniers chiffres des valeurs de la RFC 6238, dont un code qui commence par un zéro.
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void CodesPublishedInTheRfcAreAcceptedInTheirExactPeriod(long unixSeconds, string rfcCode)
    {
        var rfcDate = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;

        var foundPeriod = tool.FindValidPeriod(RfcSecret, rfcCode, rfcDate);

        Assert.Equal(unixSeconds / 30, foundPeriod);
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(0)]
    [InlineData(30)]
    public void CodeOfTheCurrentPreviousOrNextPeriodIsFoundWithItsOwnPeriodNumber(int driftSeconds)
    {
        var date = CodeDate.AddSeconds(driftSeconds);
        var expectedPeriod = new DateTimeOffset(date).ToUnixTimeSeconds() / 30;

        var foundPeriod = tool.FindValidPeriod(RfcSecret, CalculateCodeAt(date), CodeDate);

        Assert.Equal(expectedPeriod, foundPeriod);
    }

    // Par défaut, Otp.NET n'accepte que la période en cours : ce test voit si le réglage disparaît. Au-delà de 30 secondes, le code ne vaut plus.
    [Theory]
    [InlineData(-60)]
    [InlineData(60)]
    [InlineData(-3600)]
    [InlineData(3600)]
    public void CodeOfAPeriodTooFarAwayIsNotFound(int driftSeconds)
    {
        var foundPeriod = tool.FindValidPeriod(
            RfcSecret,
            CalculateCodeAt(CodeDate.AddSeconds(driftSeconds)),
            CodeDate);

        Assert.Null(foundPeriod);
    }

    // Otp.NET ne doit ni planter ni accepter un texte qui n'est pas un code, même si le service les arrête avant.
    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    [InlineData("١٢٣٤٥٦")]
    [InlineData("\0\0\0\0\0\0")]
    public void HostileTextIsNeitherAcceptedNorACrash(string hostileText)
    {
        Assert.Null(tool.FindValidPeriod(RfcSecret, hostileText, CodeDate));
    }

    [Fact]
    public void CodeCalculatedWithAnotherSecretIsNotFound()
    {
        var otherSecretCode = new Totp(Base32Encoding.ToBytes(tool.GenerateSecret())).ComputeTotp(CodeDate);
        var rightCodes = new List<string>
        {
            CalculateCodeAt(CodeDate.AddSeconds(-30)),
            CalculateCodeAt(CodeDate),
            CalculateCodeAt(CodeDate.AddSeconds(30))
        };

        // Une fois sur un million, les deux secrets donnent le même code : ce test ne dirait alors rien, sans échouer pour autant.
        if (!rightCodes.Contains(otherSecretCode))
        {
            Assert.Null(tool.FindValidPeriod(RfcSecret, otherSecretCode, CodeDate));
        }
    }

    // 20 octets donnent exactement 32 caractères Base32, sans le signe = qui gênerait certaines applications.
    [Fact]
    public void NewSecretContains160BitsAndIsWrittenInBase32()
    {
        var secret = tool.GenerateSecret();

        Assert.Matches("^[A-Z2-7]{32}$", secret);
        Assert.Equal(20, Base32Encoding.ToBytes(secret).Length);
    }

    [Fact]
    public void TwoNewSecretsAreNeverTheSame()
    {
        var drawnSecrets = new HashSet<string>();

        for (var secretNumber = 1; secretNumber <= 1000; secretNumber++)
        {
            Assert.True(drawnSecrets.Add(tool.GenerateSecret()));
        }
    }

    // Le lien doit dire exactement ce que le serveur attend : une application qui calculerait autrement ne donnerait que de mauvais codes.
    [Fact]
    public void LinkGivesTheSecretAndTheExactSettingsOfTheCodes()
    {
        var link = tool.BuildSetupLink(RfcSecret, "utilisateur@exemple.test");

        Assert.Equal(
            "otpauth://totp/WalletFlow:utilisateur%40exemple.test?secret=" + RfcSecret + "&issuer=WalletFlow&algorithm=SHA1&digits=6&period=30",
            link);
    }

    // Une adresse e-mail peut contenir ces caractères : sans encodage, elle changerait le sens du lien.
    [Theory]
    [InlineData("a+b@exemple.test", "a%2Bb%40exemple.test")]
    [InlineData("a&secret=FAUX@exemple.test", "a%26secret%3DFAUX%40exemple.test")]
    [InlineData("a#b@exemple.test", "a%23b%40exemple.test")]
    [InlineData("a?b@exemple.test", "a%3Fb%40exemple.test")]
    [InlineData("a/b@exemple.test", "a%2Fb%40exemple.test")]
    [InlineData("a:b@exemple.test", "a%3Ab%40exemple.test")]
    public void SpecialCharactersOfTheEmailAddressCannotChangeTheMeaningOfTheLink(string emailAddress, string encodedAddress)
    {
        var link = tool.BuildSetupLink(RfcSecret, emailAddress);

        Assert.Equal(
            "otpauth://totp/WalletFlow:" + encodedAddress + "?secret=" + RfcSecret + "&issuer=WalletFlow&algorithm=SHA1&digits=6&period=30",
            link);
    }
}
