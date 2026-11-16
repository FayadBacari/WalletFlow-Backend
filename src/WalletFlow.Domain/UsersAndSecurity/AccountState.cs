namespace WalletFlow.Domain.UsersAndSecurity;

// État d'un compte, tel que l'administrateur le voit (etat_compte, DD §11). Il n'est pas stocké : il se déduit
// de l'adresse confirmée, de la fin du blocage et de la demande de suppression.
public enum AccountState
{
    NotConfirmed,  // non_confirme
    Active,        // actif
    Locked,        // bloque
    BeingDeleted   // en_suppression
}

// Déduit l'état d'un compte (DD §11). Une demande de suppression passe avant tout : le compte est alors inaccessible (GR Suppression).
// Un compte non confirmé n'est jamais bloqué (SS §2).
public static class AccountStateRules
{
    // Donne l'état du compte à ce moment.
    public static AccountState FindState(User account, DateTime now)
    {
        if (account.DeletionRequestedAt != null)
        {
            return AccountState.BeingDeleted;
        }

        if (!account.IsEmailConfirmed)
        {
            return AccountState.NotConfirmed;
        }

        if (account.IsLockedAt(now))
        {
            return AccountState.Locked;
        }

        return AccountState.Active;
    }
}
