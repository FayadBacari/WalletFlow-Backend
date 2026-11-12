using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles des connexions bancaires (GR Comptes, SS §5, DD §5 et §12).
// Les caractères spéciaux sont écrits par leur code, pour que le fichier reste lisible.
public class BankConnectionRulesTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void LimitsAreTheOnesOfTheDocuments()
    {
        Assert.Equal(TimeSpan.FromDays(180), BankConnectionRules.MaximumAuthorizationDuration);
        Assert.Equal(TimeSpan.FromDays(7), BankConnectionRules.RenewalWarningDelay);
        Assert.Equal(TimeSpan.FromHours(6), BankConnectionRules.BackgroundUpdateInterval);
        Assert.Equal("EUR", BankConnectionRules.AccountCurrency);
        Assert.Equal(50, BankConnectionRules.MaximumAccountNameLength);
        Assert.Equal(100, BankConnectionRules.MaximumBankNameLength);
    }

    // DD §5.1 : 180 jours au plus, moins si la banque n'en accepte pas autant, et toujours la marge d'horloge en moins.
    [Theory]
    [InlineData(180, 180)]
    [InlineData(400, 180)]
    [InlineData(90, 90)]
    public void AuthorizationLastsTheShortestOfOneHundredEightyDaysAndTheBankMaximum(int bankMaximumDays, int expectedDays)
    {
        var end = BankConnectionRules.ChooseAuthorizationEnd(Now, TimeSpan.FromDays(bankMaximumDays));

        Assert.Equal(Now + TimeSpan.FromDays(expectedDays) - BankConnectionRules.ClockMargin, end);
    }

    // DD §5.2 : le solde comptable d'abord, puis le plus proche que la banque donne.
    [Fact]
    public void BookedBalanceIsPreferredToTheOthers()
    {
        Assert.Equal(1, BankConnectionRules.FindPreferredBalance(new List<string> { "ITAV", "CLBD", "XPCD" }));
        Assert.Equal(0, BankConnectionRules.FindPreferredBalance(new List<string> { "XPCD", "ITAV" }));
        Assert.Equal(1, BankConnectionRules.FindPreferredBalance(new List<string> { "INCONNU", "OTHR" }));
        Assert.Equal(-1, BankConnectionRules.FindPreferredBalance(new List<string> { "INCONNU" }));
        Assert.Equal(-1, BankConnectionRules.FindPreferredBalance(new List<string>()));
    }

    // SS §5 : au plus 1 000 000 000 centimes en valeur absolue, sans débordement aux extrêmes.
    [Theory]
    [InlineData(0, true)]
    [InlineData(1_000_000_000, true)]
    [InlineData(-1_000_000_000, true)]
    [InlineData(1_000_000_001, false)]
    [InlineData(-1_000_000_001, false)]
    [InlineData(long.MaxValue, false)]
    [InlineData(long.MinValue, false)]
    public void BalanceStaysWithinTheLimitsOfTheStrategy(long amountInCents, bool isValid)
    {
        Assert.Equal(isValid, BankConnectionRules.IsBalanceValid(amountInCents));
    }

    // SS §5 : un nom reçu de la banque est un contenu non fiable. Il perd ses caractères invisibles et ses espaces autour.
    [Fact]
    public void ReceivedNameLosesInvisibleCharactersAndSurroundingSpaces()
    {
        var receivedName = "  Compte" + (char)0x0000 + " cou" + (char)0x200B + "rant" + (char)0x202E + "  ";

        var name = BankConnectionRules.CleanReceivedName(receivedName, 50, "Remplacement");

        Assert.Equal("Compte courant", name);
    }

    // Une base PostgreSQL refuse le caractère nul : il ne doit jamais arriver jusqu'à elle.
    [Fact]
    public void NameOfOnlyInvisibleCharactersGetsTheReplacementName()
    {
        var receivedName = new string((char)0x0000, 3) + (char)0x200B;

        Assert.Equal("Remplacement", BankConnectionRules.CleanReceivedName(receivedName, 50, "Remplacement"));
        Assert.Equal("Remplacement", BankConnectionRules.CleanReceivedName(null, 50, "Remplacement"));
        Assert.Equal("Remplacement", BankConnectionRules.CleanReceivedName("   ", 50, "Remplacement"));
    }

    // DD §5.2 : tronqué au-delà de 50 caractères, sans couper un emoji en deux.
    [Fact]
    public void LongNameIsCutWithoutBreakingACharacter()
    {
        var emoji = char.ConvertFromUtf32(0x1F600);
        var receivedName = string.Concat(Enumerable.Repeat(emoji, 60));

        var name = BankConnectionRules.CleanReceivedName(receivedName, 50, "Remplacement");

        Assert.Equal(50, name.EnumerateRunes().Count());
        Assert.Equal(string.Concat(Enumerable.Repeat(emoji, 50)), name);
    }

    // Un morceau d'emoji mal formé disparaît : le nom gardé est toujours bien formé.
    [Fact]
    public void BrokenCharacterDisappears()
    {
        var name = BankConnectionRules.CleanReceivedName("Livret" + (char)0xD83D, 50, "Remplacement");

        Assert.Equal("Livret", name);
    }

    [Fact]
    public void NameIsNormalizedLikeTheOtherNames()
    {
        var name = BankConnectionRules.CleanReceivedName("Epargne" + (char)0x0301, 50, "Remplacement");

        Assert.Equal("Epargné", name);
    }
}
