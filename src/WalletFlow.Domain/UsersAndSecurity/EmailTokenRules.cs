namespace WalletFlow.Domain.UsersAndSecurity;

// Règle commune à tous les liens envoyés par e-mail (SS §2).
public static class EmailTokenRules
{
    // Renvoie vrai si le lien n'a pas encore servi et n'a pas expiré. Le lien expire à sa date de fin exacte.
    public static bool CheckThatTokenCanStillBeUsedAtThisDate(EmailToken tokenOfTheLinkToCheckNow, DateTime dateOfTheCurrentHttpRequest)
    {
        return tokenOfTheLinkToCheckNow.UsedAt == null && tokenOfTheLinkToCheckNow.ExpiresAt > dateOfTheCurrentHttpRequest;
    }
}
