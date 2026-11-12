namespace WalletFlow.Application.BankConnections;

// Une banque que le prestataire sait lire, avec la plus longue autorisation qu'elle accepte.
public record ProvidedBank(string Name, TimeSpan MaximumAuthorization);
