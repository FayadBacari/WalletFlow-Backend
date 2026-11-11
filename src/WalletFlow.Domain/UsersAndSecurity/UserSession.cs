namespace WalletFlow.Domain.UsersAndSecurity;

// Session ouverte par un utilisateur, gérée côté serveur.
// Correspond à la table session du MPD (DD §4.4). Les dates sont en UTC.
// Chaque valeur se donne à la création. Ensuite, seule la date de dernière activité avance.
public class UserSession
{
    private DateTime lastActivityAt;

    // id_session : valeur aléatoire d'au moins 128 bits, créée à chaque connexion.
    public string Id { get; init; } = string.Empty;

    // date_derniere_activite : la session expire après 30 minutes sans activité.
    // Donnée par l'horloge de l'application : le compilateur refuse une session créée sans elle.
    public required DateTime LastActivityAt
    {
        get => lastActivityAt;
        init => lastActivityAt = value;
    }

    // id_utilisateur
    public Guid UserId { get; init; }

    // Renvoie vrai si la session a servi il y a moins de 30 minutes (GR Auth, SS §3).
    public bool IsActiveAt(DateTime now)
    {
        return lastActivityAt + UserSessionRules.IdleTimeout > now;
    }

    // Renvoie vrai si la dernière activité enregistrée date d'au moins une minute : il faut alors l'écrire de nouveau.
    public bool ShouldRecordActivityAt(DateTime now)
    {
        return now - lastActivityAt >= UserSessionRules.ActivityRecordInterval;
    }

    // Note que la session vient de servir : ses 30 minutes repartent de cette date. La date ne recule jamais.
    public void RecordActivity(DateTime now)
    {
        if (now > lastActivityAt)
        {
            lastActivityAt = now;
        }
    }
}
