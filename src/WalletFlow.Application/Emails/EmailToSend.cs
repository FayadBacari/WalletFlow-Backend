using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Application.Emails;

// Ce qu'une préparation donne pour une demande de la file : l'e-mail à envoyer, ou rien quand il n'y a plus rien à envoyer,
// par exemple parce que le compte a été effacé depuis la demande. La file l'envoie, puis sort la demande ou note l'essai raté.
public sealed class EmailToSend
{
    // L'e-mail à envoyer, ou null : la demande sort alors de la file sans e-mail.
    public required EmailMessage? Message { get; init; }

    // Le compte à qui l'e-mail est destiné, s'il y en a un.
    public required Guid? AccountId { get; init; }

    // Pour une récupération seulement : le résultat à noter au journal si l'e-mail part, ou s'il n'y a rien à envoyer.
    public required SecurityEventResult? RecoveryResult { get; init; }

    // Un e-mail à envoyer, destiné à ce compte s'il y en a un.
    public static EmailToSend Send(EmailMessage message, Guid? accountId, SecurityEventResult? recoveryResult = null)
    {
        return new EmailToSend { Message = message, AccountId = accountId, RecoveryResult = recoveryResult };
    }

    // Rien à envoyer : la demande peut sortir de la file.
    public static EmailToSend Nothing(Guid? accountId, SecurityEventResult? recoveryResult = null)
    {
        return new EmailToSend { Message = null, AccountId = accountId, RecoveryResult = recoveryResult };
    }
}
