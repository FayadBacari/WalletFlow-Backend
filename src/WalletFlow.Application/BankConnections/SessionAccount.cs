namespace WalletFlow.Application.BankConnections;

// Un compte d'une session déjà autorisée : son identifiant pour lire ses soldes, et son identifiant stable pour le retrouver en base.
public record SessionAccount(string Uid, string StableId);
