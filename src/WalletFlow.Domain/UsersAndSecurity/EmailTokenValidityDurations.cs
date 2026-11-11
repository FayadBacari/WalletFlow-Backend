namespace WalletFlow.Domain.UsersAndSecurity;

// Durée de validité des liens envoyés par e-mail (SS §2).
public static class EmailTokenValidityDurations
{
    public static readonly TimeSpan SignupConfirmationLink = TimeSpan.FromHours(24);
    public static readonly TimeSpan PasswordRecoveryLink = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan AccountUnlockLink = TimeSpan.FromHours(1);
}
