using WalletFlow.Application.Emails;
using WalletFlow.Application.Notifications;
using WalletFlow.Infrastructure.Emails;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie le texte des quatre alertes qu'on peut désactiver (GR Notif, SS §9) : aucun montant, aucun lien, le nom de ce
// qu'elles visent dans le texte et jamais dans l'objet, et les dates en toutes lettres, à l'heure de Paris.
public class FrenchAlertEmailsTests
{
    private const string Recipient = "utilisateur@exemple.test";

    private readonly FrenchAlertEmails emails = new FrenchAlertEmails();

    // Les quatre e-mails, un de chaque type, pour vérifier ce qu'ils ont tous en commun.
    private List<EmailMessage> CreateOneOfEach()
    {
        return new List<EmailMessage>
        {
            emails.CreateEmail(Recipient, new AlertEmailContent.BudgetReached { CategoryName = "Loisirs", Month = new DateOnly(2026, 11, 1), Threshold = 80 }),
            emails.CreateEmail(Recipient, new AlertEmailContent.NewSuggestions { Month = new DateOnly(2026, 10, 1) }),
            emails.CreateEmail(Recipient, new AlertEmailContent.BagGoalReached { BagName = "Vacances" }),
            emails.CreateEmail(Recipient, new AlertEmailContent.BankAuthorizationToRenew
            {
                BankName = "Banque du Nord",
                AuthorizationExpiresAt = new DateTime(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc)
            })
        };
    }

    // GR Notif : aucune alerte ne contient de montant. SS §9 : aucun lien, pas même vers WalletFlow, et chacune dit
    // comment couper l'alerte.
    [Fact]
    public void NoAlertContainsAnAmountNorALink()
    {
        foreach (var email in CreateOneOfEach())
        {
            Assert.Equal(Recipient, email.RecipientAddress);
            Assert.DoesNotContain("€", email.Body);
            Assert.DoesNotContain("http", email.Body);
            Assert.DoesNotContain("www", email.Body);
            Assert.Contains("Tu peux couper cette alerte dans les réglages de WalletFlow.", email.Body);
            Assert.StartsWith("Bonjour,", email.Body);
        }
    }

    // Un nom choisi par l'utilisateur ou donné par la banque n'entre jamais dans l'objet : il reste dans le texte.
    [Fact]
    public void NamesAreInTheTextButNeverInTheSubject()
    {
        var all = CreateOneOfEach();

        Assert.Contains("« Loisirs »", all[0].Body);
        Assert.Contains("« Vacances »", all[2].Body);
        Assert.Contains("« Banque du Nord »", all[3].Body);
        Assert.All(all, email => Assert.DoesNotContain("«", email.Subject));
    }

    // Les deux seuils ont chacun leur phrase et leur objet.
    [Fact]
    public void EachBudgetThresholdHasItsOwnWords()
    {
        var eighty = emails.CreateEmail(Recipient, new AlertEmailContent.BudgetReached { CategoryName = "Loisirs", Month = new DateOnly(2026, 11, 1), Threshold = 80 });
        var full = emails.CreateEmail(Recipient, new AlertEmailContent.BudgetReached { CategoryName = "Loisirs", Month = new DateOnly(2026, 11, 1), Threshold = 100 });

        Assert.Contains("En novembre 2026, tes dépenses dans la catégorie « Loisirs » ont atteint 80 % de son budget du mois.", eighty.Body);
        Assert.Equal("Alerte de budget : 80 % atteints", eighty.Subject);
        Assert.Contains("ont atteint son budget du mois.", full.Body);
        Assert.DoesNotContain("100 %", full.Body);
        Assert.Equal("Alerte de budget : budget atteint", full.Subject);
    }

    // « de » s'élide devant avril, août et octobre, et devant eux seuls.
    [Theory]
    [InlineData(10, "tes opérations d'octobre 2026")]
    [InlineData(4, "tes opérations d'avril 2026")]
    [InlineData(8, "tes opérations d'août 2026")]
    [InlineData(11, "tes opérations de novembre 2026")]
    [InlineData(1, "tes opérations de janvier 2026")]
    public void SuggestionMonthIsWrittenInFrench(int month, string expectedWords)
    {
        var email = emails.CreateEmail(Recipient, new AlertEmailContent.NewSuggestions { Month = new DateOnly(2026, month, 1) });

        Assert.Contains(expectedWords, email.Body);
        Assert.Equal("De nouvelles suggestions d'épargne t'attendent", email.Subject);
    }

    // La date de fin est celle de Paris : 23 h 30 en temps universel le 31 décembre, c'est déjà le 1er janvier.
    [Theory]
    [InlineData(2026, 12, 31, 23, 30, "le 1er janvier 2027")]
    [InlineData(2026, 11, 22, 9, 0, "le 22 novembre 2026")]
    [InlineData(2027, 7, 1, 21, 59, "le 1er juillet 2027")]
    [InlineData(2027, 7, 1, 22, 0, "le 2 juillet 2027")]
    public void AuthorizationEndIsTheDayInParis(int year, int month, int day, int hour, int minute, string expectedDay)
    {
        var email = emails.CreateEmail(Recipient, new AlertEmailContent.BankAuthorizationToRenew
        {
            BankName = "Banque du Nord",
            AuthorizationExpiresAt = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc)
        });

        Assert.Contains("Sa date de fin est " + expectedDay + ".", email.Body);
    }

    // SS §9 : l'e-mail de l'autorisation n'invite qu'à ouvrir WalletFlow, et met en garde contre un lien reçu par e-mail.
    [Fact]
    public void AuthorizationEmailWarnsAgainstLinks()
    {
        var email = CreateOneOfEach()[3];

        Assert.Contains("ouvre WalletFlow et renouvelle-la", email.Body);
        Assert.Contains("ne renouvelle jamais ton autorisation depuis un lien reçu par e-mail", email.Body);
        Assert.Equal("Ton autorisation bancaire est à renouveler", email.Subject);
    }
}
