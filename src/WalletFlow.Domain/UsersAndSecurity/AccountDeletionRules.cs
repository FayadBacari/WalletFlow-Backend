namespace WalletFlow.Domain.UsersAndSecurity;

// Règles de la suppression d'un compte (GR Suppression, DD §11 et §12) : il est effacé 30 jours après la demande.
public static class AccountDeletionRules
{
    public static readonly TimeSpan ErasureDelay = TimeSpan.FromDays(30);

    // Donne le moment où le compte sera effacé, d'après la date de sa demande de suppression (DD §11, date_effacement).
    public static DateTime FindErasureMoment(DateTime deletionRequestedAt)
    {
        return deletionRequestedAt + ErasureDelay;
    }
}
