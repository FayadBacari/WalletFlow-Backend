namespace WalletFlow.Domain.UsersAndSecurity;

// Règles du blocage d'un compte après des échecs de connexion (GR Auth, SS §2, DD §3).
public static class AccountLockoutRules
{
    public const short MaximumNumberOfFailedLoginAttempts = 5;
    public const short MaximumNumberOfLockoutsCountedOnOneAccount = 8;

    public static readonly TimeSpan DurationOfTheFirstLockoutOfAnAccount = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaximumDurationOfOneLockoutOfAnAccount = TimeSpan.FromHours(24);

    // Renvoie vrai si le compte est bloqué à cette date. Le blocage finit à sa date de fin exacte.
    public static bool CheckThatAccountIsLockedAtThisDate(User accountToCheckForALockout, DateTime dateOfTheCurrentLoginAttempt)
    {
        return accountToCheckForALockout.LockoutEndsAt != null && accountToCheckForALockout.LockoutEndsAt > dateOfTheCurrentLoginAttempt;
    }

    // Compte un essai de plus, avant même de vérifier le mot de passe : des requêtes simultanées
    // ne peuvent donc pas tester plus de 5 mots de passe. Le dernier essai bloque aussitôt le compte :
    // si le serveur s'arrête ensuite, le blocage a quand même une date de fin.
    // Renvoie vrai si c'est cet essai qui bloque le compte.
    public static bool CountOneLoginAttemptAndLockAccountOnTheLastOne(User accountThatReceivesTheLoginAttempt, DateTime dateOfTheCurrentLoginAttempt)
    {
        // Le blocage précédent est terminé : le compte retrouve ses 5 essais.
        if (accountThatReceivesTheLoginAttempt.FailedLoginAttemptCount >= MaximumNumberOfFailedLoginAttempts)
        {
            accountThatReceivesTheLoginAttempt.FailedLoginAttemptCount = 0;
            accountThatReceivesTheLoginAttempt.LockoutEndsAt = null;
        }

        accountThatReceivesTheLoginAttempt.FailedLoginAttemptCount = (short)(accountThatReceivesTheLoginAttempt.FailedLoginAttemptCount + 1);

        if (accountThatReceivesTheLoginAttempt.FailedLoginAttemptCount < MaximumNumberOfFailedLoginAttempts)
        {
            return false;
        }

        if (accountThatReceivesTheLoginAttempt.LockoutCount < MaximumNumberOfLockoutsCountedOnOneAccount)
        {
            accountThatReceivesTheLoginAttempt.LockoutCount = (short)(accountThatReceivesTheLoginAttempt.LockoutCount + 1);
        }

        accountThatReceivesTheLoginAttempt.LockoutEndsAt = dateOfTheCurrentLoginAttempt + CalculateDurationOfTheLockoutWithThisNumber(accountThatReceivesTheLoginAttempt.LockoutCount);
        return true;
    }

    // Donne la durée d'un blocage : 15 minutes × 2^(nb_blocages − 1), 24 heures au plus (DD).
    public static TimeSpan CalculateDurationOfTheLockoutWithThisNumber(short numberOfLockoutsCountedOnTheAccount)
    {
        var durationOfTheLockoutBeingCalculated = DurationOfTheFirstLockoutOfAnAccount;

        for (var numberOfTheLockoutBeingDoubled = 2; numberOfTheLockoutBeingDoubled <= numberOfLockoutsCountedOnTheAccount; numberOfTheLockoutBeingDoubled++)
        {
            durationOfTheLockoutBeingCalculated = durationOfTheLockoutBeingCalculated * 2;
        }

        if (durationOfTheLockoutBeingCalculated > MaximumDurationOfOneLockoutOfAnAccount)
        {
            return MaximumDurationOfOneLockoutOfAnAccount;
        }

        return durationOfTheLockoutBeingCalculated;
    }

    // Remet tout à zéro après une connexion réussie (GR Auth).
    public static void ResetLoginCountersAfterSuccessfulLogin(User accountThatHasJustLoggedIn)
    {
        accountThatHasJustLoggedIn.FailedLoginAttemptCount = 0;
        accountThatHasJustLoggedIn.LockoutCount = 0;
        accountThatHasJustLoggedIn.LockoutEndsAt = null;
    }
}
