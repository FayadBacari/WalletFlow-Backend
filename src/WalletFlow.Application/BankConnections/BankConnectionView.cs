using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Application.BankConnections;

// Une connexion telle que l'utilisateur la voit : son état à cet instant, l'avertissement des 7 jours, et ses comptes.
// Status vaut « expirée » dès que la date de fin est passée, même si la tâche de fond ne l'a pas encore notée.
public record BankConnectionView(BankConnection Connection, BankConnectionStatus Status, bool IsEndingSoon, List<BankAccount> Accounts);
