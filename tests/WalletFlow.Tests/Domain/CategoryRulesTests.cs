using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles d'une catégorie (GR Dépenses, SS §5 et §6, DD §6.1 et §12).
// Les caractères spéciaux sont écrits par leur code, pour que le fichier reste lisible et que rien ne se perde en le copiant.
public class CategoryRulesTests
{
    // Répète un emoji : un caractère pour PostgreSQL, deux pour .NET.
    private static string RepeatEmoji(int count)
    {
        return string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x1F600), count));
    }

    [Fact]
    public void LimitsAreTheOnesOfTheDocuments()
    {
        Assert.Equal(50, CategoryRules.MaximumNameLength);
        Assert.Equal(100, CategoryRules.MaximumCountPerUser);
        Assert.Equal(1_000_000_000, CategoryRules.MaximumMonthlyBudgetInCents);
    }

    // Tapé sur deux appareils différents, un nom donne la même forme enregistrée.
    [Fact]
    public void NameIsTrimmedAndNormalized()
    {
        var decomposedName = "Sante" + (char)0x0301;
        var noBreakSpace = (char)0x00A0;

        Assert.Equal("Santé", CategoryRules.NormalizeName("  " + decomposedName + noBreakSpace));
        Assert.Equal("Vie courante", CategoryRules.NormalizeName(" Vie courante "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("     ")]
    public void MissingOrBlankNameBecomesEmptyAndIsRefused(string? typedName)
    {
        var name = CategoryRules.NormalizeName(typedName);

        Assert.Equal(string.Empty, name);
        Assert.False(CategoryRules.IsNameValid(name));
    }

    [Fact]
    public void NameFromOneToFiftyCharactersIsValid()
    {
        Assert.True(CategoryRules.IsNameValid("A"));
        Assert.True(CategoryRules.IsNameValid(new string('a', 50)));
        Assert.False(CategoryRules.IsNameValid(new string('a', 51)));
    }

    // PostgreSQL compte un emoji pour un caractère : 50 emojis tiennent dans VARCHAR(50).
    [Fact]
    public void EmojisAreCountedLikeInTheDatabase()
    {
        Assert.True(CategoryRules.IsNameValid(RepeatEmoji(50)));
        Assert.False(CategoryRules.IsNameValid(RepeatEmoji(51)));
    }

    // Un caractère invisible ferait deux catégories qui ont l'air d'avoir le même nom.
    [Theory]
    [InlineData(0x200B)]
    [InlineData(0x202E)]
    [InlineData(0x0007)]
    [InlineData(0x2028)]
    [InlineData(0xE000)]
    public void InvisibleCharacterIsRefused(int code)
    {
        var name = "Loi" + (char)code + "sirs";

        Assert.False(CategoryRules.IsNameValid(CategoryRules.NormalizeName(name)));
    }

    // .NET refuse de normaliser une moitié d'emoji : le nom est gardé tel quel, puis refusé.
    [Fact]
    public void BrokenCharacterIsRefused()
    {
        var name = CategoryRules.NormalizeName("Loisirs" + (char)0xD83D);

        Assert.False(CategoryRules.IsNameValid(name));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3000)]
    [InlineData(1_000_000_000)]
    public void BudgetFromOneCentToTenMillionEurosIsValid(int budgetInCents)
    {
        Assert.True(CategoryRules.IsMonthlyBudgetValid(budgetInCents));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_000_001)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void BudgetOutsideTheLimitsIsRefused(int budgetInCents)
    {
        Assert.False(CategoryRules.IsMonthlyBudgetValid(budgetInCents));
    }

    // Pas de budget du tout : c'est permis, le budget est facultatif.
    [Fact]
    public void MissingBudgetIsValid()
    {
        Assert.True(CategoryRules.IsMonthlyBudgetValid(null));
    }
}
