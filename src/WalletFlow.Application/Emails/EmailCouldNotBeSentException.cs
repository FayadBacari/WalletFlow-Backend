namespace WalletFlow.Application.Emails;

// Levée par l'expéditeur quand un e-mail n'a pas pu partir.
// Son message ne contient jamais l'adresse du destinataire, car il peut finir dans les journaux (SS §7).
public class EmailCouldNotBeSentException : Exception
{
    // Garde l'erreur technique d'origine.
    public EmailCouldNotBeSentException(Exception cause)
        : base("L'e-mail n'a pas pu partir.", cause)
    {
    }
}
