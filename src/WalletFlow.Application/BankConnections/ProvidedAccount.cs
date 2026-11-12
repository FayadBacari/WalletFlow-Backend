namespace WalletFlow.Application.BankConnections;

// Un compte d'une nouvelle session. Uid ne vaut que pour cette session ; StableId reste le même d'une autorisation à l'autre.
// Name est le nom du compte donné par la banque, null si elle n'en donne pas. Currency est un code ISO 4217.
public record ProvidedAccount(string Uid, string StableId, string? Name, string Currency);
