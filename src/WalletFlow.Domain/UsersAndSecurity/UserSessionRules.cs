namespace WalletFlow.Domain.UsersAndSecurity;

// Durées et limites des sessions (GR Auth, SS §3, DD §4.4 et §12). Les règles elles-mêmes sont dans la session : voir UserSession.
public static class UserSessionRules
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    // Une session ne dure jamais plus de 12 heures, même utilisée sans arrêt : une session volée finit par mourir (OWASP ASVS,
    // niveau 2, exigence 3.3.2).
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(12);

    // Au plus ce nombre de sessions ouvertes en même temps pour un compte : la connexion suivante ferme la moins récente.
    public const int MaximumSessionsPerAccount = 10;

    // L'activité n'est écrite en base qu'une fois par minute au plus : une écriture à chaque requête chargerait la base pour rien.
    // La session se ferme donc après 29 à 30 minutes sans activité, jamais plus tard.
    public static readonly TimeSpan ActivityRecordInterval = TimeSpan.FromMinutes(1);
}
