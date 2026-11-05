namespace WalletFlow.Api.Contracts;

// Réponse à la fin de l'activation : les 10 codes de secours, affichés cette seule fois (GR Auth).
public record TwoFactorBackupCodesResponse(List<string> BackupCodes);
