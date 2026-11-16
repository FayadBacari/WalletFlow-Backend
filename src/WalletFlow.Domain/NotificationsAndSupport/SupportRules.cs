using System.Text;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Domain.NotificationsAndSupport;

// Règles du support (GR Admin, SS §5 et §6, DD §10 et §12) : le texte d'un message ou d'une réponse, le nombre de demandes
// qu'un utilisateur peut envoyer, et la durée pendant laquelle une demande est gardée.
public static class SupportRules
{
    public const int MaximumTextLength = 5000;
    public const int MaximumRequestsPerDay = 10;

    // Un jour de demandes, ce sont les dernières 24 heures, pas le jour du calendrier : la limite ne repart pas à zéro à minuit.
    public static readonly TimeSpan RequestDay = TimeSpan.FromHours(24);

    // Donne la forme enregistrée d'un texte : chaque fin de ligne devient un simple retour à la ligne, quel que soit
    // l'ordinateur qui l'a écrit, puis le texte passe en Unicode NFC, sans espaces ni lignes vides autour.
    // Un texte absent devient vide, et IsTextValid le refuse. .NET refuse de normaliser un texte qui contient
    // un caractère mal formé : il est alors gardé tel quel, et IsTextValid le refusera.
    public static string NormalizeText(string? typedText)
    {
        if (typedText == null)
        {
            return string.Empty;
        }

        var text = typedText.Replace("\r\n", "\n").Replace('\r', '\n');

        try
        {
            return text.Normalize(NormalizationForm.FormC).Trim();
        }
        catch (ArgumentException)
        {
            return text.Trim();
        }
    }

    // Renvoie vrai si un texte, déjà sous sa forme enregistrée, fait de 1 à 5 000 caractères, sans caractère invisible
    // autre que le retour à la ligne (SS §5). Un caractère invisible pourrait cacher une partie du message à l'administrateur
    // qui le lit, ou en inverser le sens de lecture. Les caractères sont comptés comme PostgreSQL les compte :
    // un emoji compte pour un, pas pour deux.
    public static bool IsTextValid(string text)
    {
        var characterCount = 0;

        foreach (var character in text.EnumerateRunes())
        {
            if (character.Value != '\n' && InvisibleCharacters.IsInvisible(character))
            {
                return false;
            }

            characterCount = characterCount + 1;
        }

        return characterCount >= 1 && characterCount <= MaximumTextLength;
    }

    // Donne le moment avant lequel une demande envoyée n'est plus gardée : un an avant maintenant (GR Admin).
    public static DateTime FindOldestKeptSendingTime(DateTime now)
    {
        return now.AddYears(-1);
    }
}
