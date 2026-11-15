using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.BankConnections;

// Choisit, parmi les soldes donnés par la banque, celui que WalletFlow garde (DD §5.2) : en euros, lisible,
// dans les limites de la SS §5, et du type préféré (voir BankConnectionRules).
public static class ProvidedBalanceChoice
{
    // Renvoie le montant choisi en centimes, ou null si aucun solde ne convient.
    public static int? Choose(List<ProvidedBalance> balances)
    {
        var usableBalances = new List<(string Type, long AmountInCents)>();

        foreach (var balance in balances)
        {
            if (balance.Currency == BankConnectionRules.AccountCurrency
                && balance.AmountInCents is long amountInCents
                && BankConnectionRules.IsBalanceValid(amountInCents))
            {
                usableBalances.Add((balance.Type, amountInCents));
            }
        }

        var position = BankConnectionRules.FindPreferredBalance(usableBalances.Select(usable => usable.Type).ToList());

        if (position < 0)
        {
            return null;
        }

        return (int)usableBalances[position].AmountInCents;
    }
}
