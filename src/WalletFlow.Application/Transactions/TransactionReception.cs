using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WalletFlow.Application.BankConnections;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.Transactions;

// Vérifie les opérations données par la banque, et donne à chacune son identifiant (GR Comptes, DD §5.3).
// Seules les opérations comptabilisées, en euros, datées et d'un montant permis sont gardées : une opération en attente
// arrivera quand la banque l'aura comptabilisée.
public static class TransactionReception
{
    // Garde les opérations valables, dans l'ordre donné par la banque, chacune avec son libellé enregistré et son identifiant.
    public static List<ReceivedTransaction> Receive(List<ProvidedTransaction> providedTransactions)
    {
        var receivedTransactions = new List<ReceivedTransaction>();
        var countByFingerprint = new Dictionary<string, int>();

        foreach (var provided in providedTransactions)
        {
            if (!provided.IsBooked
                || provided.Currency != BankConnectionRules.AccountCurrency
                || provided.Date is not DateOnly date
                || !TransactionRules.IsDateValid(date)
                || provided.AmountInCents is not long amountInCents
                || !TransactionRules.IsAmountValid(amountInCents))
            {
                continue;
            }

            var label = TransactionRules.CleanLabel(provided.Label);
            var fingerprint = ComputeFingerprint(provided.Reference, date, amountInCents, label);
            var rank = countByFingerprint.GetValueOrDefault(fingerprint);
            countByFingerprint[fingerprint] = rank + 1;

            receivedTransactions.Add(new ReceivedTransaction(fingerprint + ":" + rank, date, (int)amountInCents, label));
        }

        return receivedTransactions;
    }

    // L'identifiant d'une opération ne peut pas être la seule référence de la banque : certaines n'en donnent pas,
    // d'autres donnent la même à plusieurs opérations. C'est donc l'empreinte SHA-256 de la référence, de la date
    // et du montant, plus le libellé quand la banque ne donne pas de référence : la même opération relue donne toujours
    // la même empreinte. Des opérations identiques du même jour ont la même empreinte : leur rang dans la lecture,
    // ajouté après, les distingue. Une lecture couvre toujours des jours entiers, donc toutes les opérations de ces jours.
    private static string ComputeFingerprint(string? reference, DateOnly date, long amountInCents, string label)
    {
        var parts = new List<string>
        {
            reference ?? string.Empty,
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            amountInCents.ToString(CultureInfo.InvariantCulture)
        };

        if (string.IsNullOrWhiteSpace(reference))
        {
            parts.Add(label);
        }

        var text = JsonSerializer.Serialize(parts);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
