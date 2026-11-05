namespace WalletFlow.Domain.UsersAndSecurity;

// Durée de validité des liens envoyés par e-mail (SS §2).
public static class EmailTokenValidityDurations
{
    public static readonly TimeSpan ValidityDurationOfTheSignupConfirmationLink = TimeSpan.FromHours(24);
    public static readonly TimeSpan ValidityDurationOfThePasswordRecoveryLink = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ValidityDurationOfTheAccountUnlockLink = TimeSpan.FromHours(1);
}
