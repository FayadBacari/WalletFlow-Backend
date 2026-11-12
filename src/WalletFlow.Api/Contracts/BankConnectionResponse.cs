namespace WalletFlow.Api.Contracts;

// Une connexion bancaire telle que l'utilisateur la voit. Status vaut « active », « inError » ou « expired ».
// IsEndingSoon dit que l'autorisation finit dans 7 jours ou moins : le site prévient l'utilisateur (GR Comptes).
public record BankConnectionResponse(
    Guid Id,
    string BankName,
    string Status,
    DateTime AuthorizationEndsAt,
    bool IsEndingSoon,
    List<BankAccountResponse> Accounts);
