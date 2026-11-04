namespace WalletFlow.Application.Emails;

// Un e-mail à envoyer : son destinataire, son objet et son texte.
public record EmailMessageToSend(string RecipientAddress, string Subject, string Body);
