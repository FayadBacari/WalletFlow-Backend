using OtpNet;
using WalletFlow.Infrastructure.Security;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le calcul des codes (SS §2), avec les valeurs publiées par la RFC 6238 : une vraie application d'authentification calcule les mêmes.
public class OtpNetAuthenticatorCodeToolTests
{
    // Le secret de la RFC 6238 : « 12345678901234567890 », écrit en Base32.
    private const string SecretOfThePhonePublishedInTheRfc = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private static readonly DateTime DateOfTheCodesUsedInTheseTests = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    private readonly OtpNetAuthenticatorCodeTool toolBeingTestedWithRealCalculations = new OtpNetAuthenticatorCodeTool();

    // Calcule le code qu'affiche un téléphone à cette date.
    private static string CalculateCodeShownOnThePhoneAtThisDate(DateTime dateShownOnThePhoneInUtc)
    {
        return new Totp(Base32Encoding.ToBytes(SecretOfThePhonePublishedInTheRfc)).ComputeTotp(dateShownOnThePhoneInUtc);
    }

    // Les 6 derniers chiffres des valeurs de la RFC 6238, dont un code qui commence par un zéro.
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void CodesPublishedInTheRfcAreAcceptedInTheirExactPeriod(long numberOfSecondsSinceTheUnixEpoch, string codeOfThePhonePublishedInTheRfc)
    {
        var dateOfTheCodePublishedInTheRfc = DateTimeOffset.FromUnixTimeSeconds(numberOfSecondsSinceTheUnixEpoch).UtcDateTime;

        var numberOfThePeriodFoundByTheTool = toolBeingTestedWithRealCalculations.FindPeriodWhereThisCodeIsValidAtThisDate(SecretOfThePhonePublishedInTheRfc, codeOfThePhonePublishedInTheRfc, dateOfTheCodePublishedInTheRfc);

        Assert.Equal(numberOfSecondsSinceTheUnixEpoch / 30, numberOfThePeriodFoundByTheTool);
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(0)]
    [InlineData(30)]
    public void CodeOfTheCurrentPreviousOrNextPeriodIsFoundWithItsOwnPeriodNumber(int secondsBetweenThePhoneAndTheServer)
    {
        var dateShownOnThePhoneOfTheUser = DateOfTheCodesUsedInTheseTests.AddSeconds(secondsBetweenThePhoneAndTheServer);
        var periodNumberExpectedByThisTest = new DateTimeOffset(dateShownOnThePhoneOfTheUser).ToUnixTimeSeconds() / 30;

        var numberOfThePeriodFoundByTheTool = toolBeingTestedWithRealCalculations.FindPeriodWhereThisCodeIsValidAtThisDate(SecretOfThePhonePublishedInTheRfc, CalculateCodeShownOnThePhoneAtThisDate(dateShownOnThePhoneOfTheUser), DateOfTheCodesUsedInTheseTests);

        Assert.Equal(periodNumberExpectedByThisTest, numberOfThePeriodFoundByTheTool);
    }

    // Par défaut, Otp.NET n'accepte que la période en cours : ce test voit si le réglage disparaît. Au-delà de 30 secondes, le code ne vaut plus.
    [Theory]
    [InlineData(-60)]
    [InlineData(60)]
    [InlineData(-3600)]
    [InlineData(3600)]
    public void CodeOfAPeriodTooFarAwayIsNotFound(int secondsBetweenThePhoneAndTheServer)
    {
        var numberOfThePeriodFoundByTheTool = toolBeingTestedWithRealCalculations.FindPeriodWhereThisCodeIsValidAtThisDate(
            SecretOfThePhonePublishedInTheRfc,
            CalculateCodeShownOnThePhoneAtThisDate(DateOfTheCodesUsedInTheseTests.AddSeconds(secondsBetweenThePhoneAndTheServer)),
            DateOfTheCodesUsedInTheseTests);

        Assert.Null(numberOfThePeriodFoundByTheTool);
    }

    // Otp.NET ne doit ni planter ni accepter un texte qui n'est pas un code, même si le service les arrête avant.
    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    [InlineData("١٢٣٤٥٦")]
    [InlineData("\0\0\0\0\0\0")]
    public void HostileTextIsNeitherAcceptedNorACrash(string hostileTextSentInThisTest)
    {
        Assert.Null(toolBeingTestedWithRealCalculations.FindPeriodWhereThisCodeIsValidAtThisDate(SecretOfThePhonePublishedInTheRfc, hostileTextSentInThisTest, DateOfTheCodesUsedInTheseTests));
    }

    [Fact]
    public void CodeCalculatedWithAnotherSecretIsNotFound()
    {
        var codeCalculatedWithAnotherAuthenticatorSecret = new Totp(Base32Encoding.ToBytes(toolBeingTestedWithRealCalculations.GenerateNewRandomSecretForTheAuthenticatorApplication())).ComputeTotp(DateOfTheCodesUsedInTheseTests);
        var codesCalculatedWithTheRightAuthenticatorSecret = new List<string>
        {
            CalculateCodeShownOnThePhoneAtThisDate(DateOfTheCodesUsedInTheseTests.AddSeconds(-30)),
            CalculateCodeShownOnThePhoneAtThisDate(DateOfTheCodesUsedInTheseTests),
            CalculateCodeShownOnThePhoneAtThisDate(DateOfTheCodesUsedInTheseTests.AddSeconds(30))
        };

        // Une fois sur un million, les deux secrets donnent le même code : ce test ne dirait alors rien, sans échouer pour autant.
        if (!codesCalculatedWithTheRightAuthenticatorSecret.Contains(codeCalculatedWithAnotherAuthenticatorSecret))
        {
            Assert.Null(toolBeingTestedWithRealCalculations.FindPeriodWhereThisCodeIsValidAtThisDate(SecretOfThePhonePublishedInTheRfc, codeCalculatedWithAnotherAuthenticatorSecret, DateOfTheCodesUsedInTheseTests));
        }
    }

    // 20 octets donnent exactement 32 caractères Base32, sans le signe = qui gênerait certaines applications.
    [Fact]
    public void NewSecretContains160BitsAndIsWrittenInBase32()
    {
        var newSecretOfTheAuthenticatorApplication = toolBeingTestedWithRealCalculations.GenerateNewRandomSecretForTheAuthenticatorApplication();

        Assert.Matches("^[A-Z2-7]{32}$", newSecretOfTheAuthenticatorApplication);
        Assert.Equal(20, Base32Encoding.ToBytes(newSecretOfTheAuthenticatorApplication).Length);
    }

    [Fact]
    public void TwoNewSecretsAreNeverTheSame()
    {
        var secretsAlreadyDrawnInThisTest = new HashSet<string>();

        for (var numberOfTheSecretBeingDrawn = 1; numberOfTheSecretBeingDrawn <= 1000; numberOfTheSecretBeingDrawn++)
        {
            Assert.True(secretsAlreadyDrawnInThisTest.Add(toolBeingTestedWithRealCalculations.GenerateNewRandomSecretForTheAuthenticatorApplication()));
        }
    }

    // Le lien doit dire exactement ce que le serveur attend : une application qui calculerait autrement ne donnerait que de mauvais codes.
    [Fact]
    public void LinkGivesTheSecretAndTheExactSettingsOfTheCodes()
    {
        var linkGivenToTheAuthenticatorApplicationByTheTool = toolBeingTestedWithRealCalculations.BuildLinkThatAddsThisSecretToTheApplication(SecretOfThePhonePublishedInTheRfc, "utilisateur@exemple.test");

        Assert.Equal(
            "otpauth://totp/WalletFlow:utilisateur%40exemple.test?secret=" + SecretOfThePhonePublishedInTheRfc + "&issuer=WalletFlow&algorithm=SHA1&digits=6&period=30",
            linkGivenToTheAuthenticatorApplicationByTheTool);
    }

    // Une adresse e-mail peut contenir ces caractères : sans encodage, elle changerait le sens du lien.
    [Theory]
    [InlineData("a+b@exemple.test", "a%2Bb%40exemple.test")]
    [InlineData("a&secret=FAUX@exemple.test", "a%26secret%3DFAUX%40exemple.test")]
    [InlineData("a#b@exemple.test", "a%23b%40exemple.test")]
    [InlineData("a?b@exemple.test", "a%3Fb%40exemple.test")]
    [InlineData("a/b@exemple.test", "a%2Fb%40exemple.test")]
    [InlineData("a:b@exemple.test", "a%3Ab%40exemple.test")]
    public void SpecialCharactersOfTheEmailAddressCannotChangeTheMeaningOfTheLink(string emailAddressOfTheAccountInThisTest, string encodedAddressExpectedInTheLink)
    {
        var linkGivenToTheAuthenticatorApplicationByTheTool = toolBeingTestedWithRealCalculations.BuildLinkThatAddsThisSecretToTheApplication(SecretOfThePhonePublishedInTheRfc, emailAddressOfTheAccountInThisTest);

        Assert.Equal(
            "otpauth://totp/WalletFlow:" + encodedAddressExpectedInTheLink + "?secret=" + SecretOfThePhonePublishedInTheRfc + "&issuer=WalletFlow&algorithm=SHA1&digits=6&period=30",
            linkGivenToTheAuthenticatorApplicationByTheTool);
    }
}
