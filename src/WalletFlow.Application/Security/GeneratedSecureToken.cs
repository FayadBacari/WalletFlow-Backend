namespace WalletFlow.Application.Security;

// Un jeton tout juste créé : le jeton part dans le lien de l'e-mail, seule son empreinte va en base (SS §2).
public record GeneratedSecureToken(string TokenToSendInTheEmailLink, string HashToStoreInTheDatabase);
