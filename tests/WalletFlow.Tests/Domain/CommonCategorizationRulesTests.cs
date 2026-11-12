using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles communes fournies par l'application (GR Dépenses, DD §6.2 et §12).
public class CommonCategorizationRulesTests
{
    // Chaque règle est écrite sous forme simple : sinon elle ne reconnaîtrait jamais rien.
    [Fact]
    public void EveryRuleIsWrittenInSimpleForm()
    {
        foreach (var rule in CommonCategorizationRules.All)
        {
            Assert.NotEqual(string.Empty, rule.Words);
            Assert.Equal(TransactionLabels.Simplify(rule.Words), rule.Words);
        }
    }

    // Chaque règle vise une catégorie par défaut.
    [Fact]
    public void EveryRuleNamesADefaultCategory()
    {
        var defaultNames = DefaultCategories.CreateForUser(Guid.NewGuid()).Select(category => category.Name).ToList();

        foreach (var rule in CommonCategorizationRules.All)
        {
            Assert.Contains(rule.CategoryName, defaultNames);
        }
    }

    // Une règle qui vise « Autres » ne sert qu'en exception : elle contient les mots d'une règle plus courte, qu'elle remplace.
    // Sans cela, elle ne servirait à rien : ce qu'aucune règle ne reconnaît va déjà dans « Autres ».
    [Fact]
    public void RuleToOthersIsAnExceptionOfAShorterRule()
    {
        var exceptions = CommonCategorizationRules.All.Where(rule => rule.CategoryName == "Autres").ToList();

        Assert.NotEmpty(exceptions);

        foreach (var exception in exceptions)
        {
            Assert.Contains(CommonCategorizationRules.All, rule =>
                rule.CategoryName != "Autres"
                && rule.Words.Length < exception.Words.Length
                && TransactionLabels.ContainsWords(exception.Words, rule.Words));
        }
    }

    // Deux règles ne visent jamais les mêmes mots : la seconde ne servirait à rien.
    [Fact]
    public void NoTwoRulesHaveTheSameWords()
    {
        var words = CommonCategorizationRules.All.Select(rule => rule.Words).ToList();

        Assert.Equal(words.Count, words.Distinct().Count());
    }

    // GR Dépenses : « CARREFOUR » → Alimentation, quelles que soient les majuscules, la ponctuation et les chiffres autour.
    [Theory]
    [InlineData("CB CARREFOUR MARKET 12/11", "Alimentation")]
    [InlineData("cb carrefour market", "Alimentation")]
    [InlineData("PRLV SEPA EDF CLIENTS", "Logement")]
    [InlineData("Pharmacie de l'Église", "Santé")]
    [InlineData("VIR SEPA SALAIRE NOVEMBRE", "Revenus")]
    [InlineData("PAIEMENT CB NETFLIX.COM", "Loisirs")]
    [InlineData("CB SNCF INTERNET", "Transport")]
    [InlineData("AMAZON PAYMENTS EUROPE", "Shopping")]
    [InlineData("PRLV SEPA ORANGE SA", "Logement")]
    [InlineData("PRLV CARREFOUR BANQUE 12/11", "Autres")]
    public void KnownWordsGiveTheirCategory(string label, string expectedCategoryName)
    {
        Assert.Equal(expectedCategoryName, CommonCategorizationRules.FindCategoryName(label));
    }

    // Quand plusieurs règles reconnaissent le libellé, la plus longue l'emporte : « UBER EATS » passe avant « UBER ».
    [Fact]
    public void LongestRuleWins()
    {
        Assert.Equal("Alimentation", CommonCategorizationRules.FindCategoryName("CB UBER EATS PARIS"));
        Assert.Equal("Transport", CommonCategorizationRules.FindCategoryName("CB UBER BV TRIP"));
        Assert.Equal("Alimentation", CommonCategorizationRules.FindCategoryName("CB UBER TRIP UBER EATS"));
    }

    // Un mot reconnu doit être entier : « CARREFOURS » ou « PAIEMENT » ne sont pas « CARREFOUR » ou « PAIE ».
    // La ville d'Orange n'est pas l'opérateur, et un versement sur un plan d'épargne retraite n'est pas un revenu.
    [Theory]
    [InlineData("CB BRASSERIE DU THEATRE ORANGE")]
    [InlineData("VERSEMENT PER RETRAITE")]
    [InlineData("CB CARREFOURS")]
    [InlineData("PAIEMENT CB 12/11")]
    [InlineData("CAFE DE LA GARE")]
    [InlineData("VIR M DUPONT")]
    [InlineData("")]
    public void UnknownLabelGivesNoCategory(string label)
    {
        Assert.Null(CommonCategorizationRules.FindCategoryName(label));
    }
}
