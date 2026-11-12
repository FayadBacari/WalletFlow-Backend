namespace WalletFlow.Application.AccountSettings;

// Résultat de la vérification de l'identité avant une action sensible.
public class AccountIdentityCheckResult
{
    public required bool IsConfirmed { get; init; }

    // Le code de secours reconnu, s'il y en a un : il ne sera marqué utilisé qu'avec les autres changements.
    public Guid? BackupCodeId { get; init; }
}
