namespace WalletFlow.Application.AccountSettings;

// Ce que l'utilisateur connecté voit de son compte (GR Compte).
public class AccountProfile
{
    public required string Email { get; init; }
    public string? LastName { get; init; }
    public string? FirstName { get; init; }
    public required bool IsTwoFactorEnabled { get; init; }
}
