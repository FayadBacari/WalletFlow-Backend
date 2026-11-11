namespace WalletFlow.Domain.UsersAndSecurity;

// Durée de la règle de fermeture d'une session (GR Auth, SS §3, DD §4.4). La règle elle-même est dans la session : voir UserSession.
public static class UserSessionRules
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    // L'activité n'est écrite en base qu'une fois par minute au plus : une écriture à chaque requête chargerait la base pour rien.
    // La session se ferme donc après 29 à 30 minutes sans activité, jamais plus tard.
    public static readonly TimeSpan ActivityRecordInterval = TimeSpan.FromMinutes(1);
}
