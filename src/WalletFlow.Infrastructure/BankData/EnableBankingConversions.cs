using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WalletFlow.Application.BankConnections;

namespace WalletFlow.Infrastructure.BankData;

// Traduit ce qu'envoie Enable Banking dans les termes de WalletFlow : opérations, libellés, dates, montants en centimes et
// identifiant stable des comptes. Ces conversions ne dépendent que de ce qu'elles reçoivent : elles n'appellent jamais le prestataire.
public static class EnableBankingConversions
{
    // Les statuts d'opération et les sens de l'argent d'Enable Banking (codes ISO 20022).
    public const string BookedStatus = "BOOK";
    public const string CreditIndicator = "CRDT";
    public const string DebitIndicator = "DBIT";

    // Montant le plus grand lu en texte : bien au-delà des limites de la SS §5, assez petit pour ne jamais déborder.
    private const decimal LargestReadAmount = 1_000_000_000_000m;

    // Traduit une opération d'Enable Banking. Le sens vient de l'indicateur crédit ou débit, jamais du signe du montant :
    // un montant sans sens connu est illisible. La date est la date comptable, ou à défaut la date de valeur.
    internal static ProvidedTransaction ReadTransaction(TransactionResource transaction)
    {
        var amountInCents = ReadCents(transaction.TransactionAmount?.Amount);
        long? signedAmountInCents = null;

        if (amountInCents is long amount && transaction.CreditDebitIndicator == CreditIndicator)
        {
            signedAmountInCents = Math.Abs(amount);
        }

        if (amountInCents is long debitAmount && transaction.CreditDebitIndicator == DebitIndicator)
        {
            signedAmountInCents = -Math.Abs(debitAmount);
        }

        return new ProvidedTransaction(
            transaction.EntryReference,
            transaction.Status == BookedStatus,
            ReadDate(transaction.BookingDate) ?? ReadDate(transaction.ValueDate),
            signedAmountInCents,
            transaction.TransactionAmount?.Currency ?? string.Empty,
            ChooseLabel(transaction));
    }

    // Le libellé : le texte que la banque joint à l'opération, sinon le nom de l'autre partie, sinon le type d'opération.
    private static string ChooseLabel(TransactionResource transaction)
    {
        var lines = new List<string>();

        foreach (var line in transaction.RemittanceInformation ?? new List<string?>())
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        if (lines.Count > 0)
        {
            return string.Join(" ", lines);
        }

        var otherPartyName = transaction.CreditDebitIndicator == CreditIndicator
            ? transaction.Debtor?.Name
            : transaction.Creditor?.Name;

        if (!string.IsNullOrWhiteSpace(otherPartyName))
        {
            return otherPartyName;
        }

        return transaction.BankTransactionCode?.Description ?? string.Empty;
    }

    // Lit une date écrite comme « 2026-11-12 ». Renvoie null si elle est absente ou illisible.
    private static DateOnly? ReadDate(string? date)
    {
        if (DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var readDate))
        {
            return readDate;
        }

        return null;
    }

    // L'identifiant stable d'Enable Banking peut dépasser les 100 caractères de id_compte_externe : on garde son empreinte
    // SHA-256, toujours de 64 caractères, et toujours la même pour le même compte.
    public static string ComputeStableId(string identificationHash)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identificationHash)));
    }

    // Lit un montant écrit en texte, comme « -12.34 », en centimes. Renvoie null s'il n'est pas un nombre de centimes lisible.
    public static long? ReadCents(string? amount)
    {
        if (!decimal.TryParse(amount, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || value > LargestReadAmount
            || value < -LargestReadAmount)
        {
            return null;
        }

        var cents = value * 100;

        if (cents != decimal.Truncate(cents))
        {
            return null;
        }

        return (long)cents;
    }
}
