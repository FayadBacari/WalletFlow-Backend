using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Tests.Domain;

// Vérifie les règles du support (GR Admin, SS §5 et §6, DD §10) : le texte d'un message, la durée de conservation,
// et la création d'une demande par un utilisateur ou par un visiteur.
public class SupportRulesTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);

    // Chaque fin de ligne devient un simple retour à la ligne, et le texte perd ses espaces et ses lignes vides autour.
    [Theory]
    [InlineData(null, "")]
    [InlineData("  Bonjour  ", "Bonjour")]
    [InlineData("\r\n\r\nBonjour\r\n\r\n", "Bonjour")]
    [InlineData("ligne 1\r\nligne 2\rligne 3\nligne 4", "ligne 1\nligne 2\nligne 3\nligne 4")]
    public void TextIsNormalized(string? typedText, string expectedText)
    {
        Assert.Equal(expectedText, SupportRules.NormalizeText(typedText));
    }

    // Un « é » écrit en deux morceaux (e + accent) devient un seul caractère, comme un mot de passe ou un nom.
    [Fact]
    public void TextIsWrittenInUnicodeNfc()
    {
        var combiningAcuteAccent = (char)0x0301;

        Assert.Equal("été", SupportRules.NormalizeText("e" + combiningAcuteAccent + "te" + combiningAcuteAccent));
    }

    // .NET refuse de normaliser un morceau de caractère mal formé : le texte est gardé tel quel, puis refusé.
    [Fact]
    public void MalformedTextIsKeptThenRefused()
    {
        var loneHalfOfACharacter = (char)0xD800;

        var normalized = SupportRules.NormalizeText(" a" + loneHalfOfACharacter + "b ");

        Assert.Equal("a" + loneHalfOfACharacter + "b", normalized);
        Assert.False(SupportRules.IsTextValid(normalized));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("Bonjour,\nmon compte est bloqué.\n\nMerci")]
    [InlineData("Rien de caché : 🔒 « été » — 100 %")]
    public void VisibleTextWithLineBreaksIsValid(string text)
    {
        Assert.True(SupportRules.IsTextValid(text));
    }

    [Fact]
    public void EmptyTextIsRefused()
    {
        Assert.False(SupportRules.IsTextValid(string.Empty));
    }

    // Un caractère invisible pourrait cacher une partie du message à l'administrateur, ou en inverser le sens de lecture.
    // Seul le retour à la ligne est permis. Les caractères sont créés par leur code : le fichier du test n'en contient aucun.
    [Theory]
    [InlineData(0x0009)] // tabulation
    [InlineData(0x0000)] // caractère nul, que PostgreSQL refuse
    [InlineData(0x000D)] // retour chariot seul, qui ne vient jamais d'un texte normalisé
    [InlineData(0x200B)] // espace sans largeur
    [InlineData(0x202E)] // inversion du sens de lecture
    [InlineData(0x2066)] // isolement du sens de lecture
    [InlineData(0x00AD)] // trait d'union conditionnel
    [InlineData(0x2028)] // séparateur de ligne
    [InlineData(0xD800)] // morceau de caractère mal formé
    [InlineData(0xE000)] // zone privée
    public void InvisibleCharacterOtherThanTheLineBreakIsRefused(int code)
    {
        Assert.False(SupportRules.IsTextValid("a" + (char)code + "b"));
    }

    // 5 000 caractères au plus, comptés comme PostgreSQL les compte : un emoji compte pour un, pas pour deux.
    [Fact]
    public void TextHasAtMostFiveThousandCharactersCountedLikePostgreSql()
    {
        Assert.True(SupportRules.IsTextValid(new string('a', 5000)));
        Assert.False(SupportRules.IsTextValid(new string('a', 5001)));
        Assert.True(SupportRules.IsTextValid(string.Concat(Enumerable.Repeat("😀", 5000))));
        Assert.False(SupportRules.IsTextValid(string.Concat(Enumerable.Repeat("😀", 5001))));
        Assert.True(SupportRules.IsTextValid(string.Concat(Enumerable.Repeat("a\n", 2500))[..^1] + "b"));
    }

    // Une demande est gardée un an après son envoi : la date du calendrier, même un 29 février.
    [Theory]
    [InlineData(2026, 11, 16, 2025, 11, 16)]
    [InlineData(2028, 2, 29, 2027, 2, 28)]
    public void RequestIsKeptOneYearAfterItWasSent(int year, int month, int day, int keptYear, int keptMonth, int keptDay)
    {
        var now = new DateTime(year, month, day, 9, 30, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(keptYear, keptMonth, keptDay, 9, 30, 0, DateTimeKind.Utc), SupportRules.FindOldestKeptSendingTime(now));
    }

    // La demande d'un utilisateur part envoyée, à son nom, sans adresse de contact.
    [Fact]
    public void UserRequestIsSentInTheUsersName()
    {
        var userId = Guid.NewGuid();

        var request = SupportRequest.FromUser(userId, "Où est mon sac ?", Now);

        Assert.Equal((userId, (string?)null, "Où est mon sac ?", Now, SupportRequestStatus.Sent),
            (request.UserId, request.ContactEmail, request.Message, request.SentAt, request.Status));
    }

    // La demande d'un visiteur garde l'adresse où lui répondre, et n'a pas d'utilisateur.
    [Fact]
    public void VisitorRequestKeepsTheContactAddress()
    {
        var request = SupportRequest.FromVisitor("visiteur@exemple.test", "Mon compte est bloqué.", Now);

        Assert.Equal(((Guid?)null, "visiteur@exemple.test", SupportRequestStatus.Sent),
            (request.UserId, request.ContactEmail, request.Status));
    }

    // Une demande ne se crée qu'avec un message déjà sous sa forme enregistrée, et qui respecte la règle du texte.
    public static TheoryData<string> MessagesThatBreakTheRules()
    {
        return new TheoryData<string> { " Bonjour", "a\r\nb", string.Empty, "a" + (char)0x202E + "b" };
    }

    [Theory]
    [MemberData(nameof(MessagesThatBreakTheRules))]
    public void RequestRefusesAMessageThatBreaksTheRules(string message)
    {
        Assert.Throws<ArgumentException>(() => SupportRequest.FromUser(Guid.NewGuid(), message, Now));
        Assert.Throws<ArgumentException>(() => SupportRequest.FromVisitor("visiteur@exemple.test", message, Now));
    }

    // L'adresse de contact doit être valide et déjà en minuscules : c'est elle qui recevra la réponse.
    [Theory]
    [InlineData("Visiteur@Exemple.test")]
    [InlineData("pas-une-adresse")]
    [InlineData("")]
    public void VisitorRequestRefusesAContactAddressThatBreaksTheRules(string contactEmail)
    {
        Assert.Throws<ArgumentException>(() => SupportRequest.FromVisitor(contactEmail, "Bonjour", Now));
    }
}
