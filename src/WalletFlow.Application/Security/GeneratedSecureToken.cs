namespace WalletFlow.Application.Security;

// Un jeton tout juste créé : le jeton part dans un lien d'e-mail ou dans un cookie, seule son empreinte va en base (SS §2).
public record GeneratedSecureToken(string TokenGivenOnlyToTheVisitor, string HashToStoreInTheDatabase);
