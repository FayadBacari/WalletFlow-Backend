using System.Text;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Domain.AccountsAndCategories;

// Règles des connexions bancaires (GR Comptes, SS §5, DD §5 et §12).
public static class BankConnectionRules
{
    // Une autorisation dure au plus 180 jours ; certaines banques acceptent moins (DD §5.1).
    public static readonly TimeSpan MaximumAuthorizationDuration = TimeSpan.FromDays(180);

    // L'utilisateur est prévenu 7 jours avant la fin de l'autorisation (GR Comptes).
    public static readonly TimeSpan RenewalWarningDelay = TimeSpan.FromDays(7);

    // Jusqu'à 4 mises à jour par jour en arrière-plan : au plus une toutes les 6 heures pour une connexion (GR Comptes).
    public static readonly TimeSpan BackgroundUpdateInterval = TimeSpan.FromHours(6);

    // Seuls les comptes en euros sont importés : tous les montants de WalletFlow sont des centimes d'euro.
    public const string AccountCurrency = "EUR";

    public const int MaximumBankNameLength = 100;
    public const int MaximumAccountNameLength = 50;
    public const int MaximumExternalIdLength = 100;
    public const int MaximumBalanceInCents = 1_000_000_000;
    public const string AccountNameReplacement = "Compte bancaire";
    public const string BankNameReplacement = "Banque";

    // Les types de solde, du préféré au moins bon (codes ISO 20022) : d'abord le solde comptable, avec les opérations
    // déjà passées, puis le plus proche que la banque donne.
    public static readonly IReadOnlyList<string> PreferredBalanceTypes = new List<string>
    {
        "CLBD", "ITBD", "OPBD", "XPCD", "ITAV", "CLAV", "OPAV", "FWAV", "VALU", "PRCD", "INFO", "OTHR"
    };

    // Marge retirée de la durée demandée : une horloge un peu en avance sur celle du prestataire ne dépasse jamais le maximum de la banque.
    public static readonly TimeSpan ClockMargin = TimeSpan.FromMinutes(10);

    // Choisit la fin de l'autorisation : 180 jours, ou moins si la banque n'en accepte pas autant, moins la marge d'horloge.
    public static DateTime ChooseAuthorizationEnd(DateTime now, TimeSpan bankMaximum)
    {
        if (bankMaximum < MaximumAuthorizationDuration)
        {
            return now + bankMaximum - ClockMargin;
        }

        return now + MaximumAuthorizationDuration - ClockMargin;
    }

    // Donne la position, dans cette liste, du type de solde préféré. Renvoie -1 si aucun type n'est connu.
    public static int FindPreferredBalance(List<string> balanceTypes)
    {
        foreach (var preferredType in PreferredBalanceTypes)
        {
            var position = balanceTypes.IndexOf(preferredType);

            if (position >= 0)
            {
                return position;
            }
        }

        return -1;
    }

    // Renvoie vrai si le montant tient dans les limites de la SS §5 : au plus 1 000 000 000 centimes en valeur absolue.
    public static bool IsBalanceValid(long amountInCents)
    {
        return amountInCents >= -MaximumBalanceInCents
            && amountInCents <= MaximumBalanceInCents;
    }

    // Donne la forme enregistrée d'un nom reçu de la banque, contenu non fiable (SS §5) : sans caractère invisible,
    // en Unicode NFC, sans espaces autour, et coupé à cette longueur sans casser un caractère (DD §5.2).
    // Un nom vide reçoit le nom de remplacement.
    public static string CleanReceivedName(string? receivedName, int maximumLength, string replacementName)
    {
        var visibleName = InvisibleCharacters.Remove(receivedName ?? string.Empty)
            .Normalize(NormalizationForm.FormC)
            .Trim();
        var cutName = new StringBuilder();
        var characterCount = 0;

        foreach (var character in visibleName.EnumerateRunes())
        {
            if (characterCount == maximumLength)
            {
                break;
            }

            cutName.Append(character.ToString());
            characterCount = characterCount + 1;
        }

        var savedName = cutName.ToString().TrimEnd();

        if (savedName.Length == 0)
        {
            return replacementName;
        }

        return savedName;
    }
}
