using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace WalletFlow.Infrastructure.Persistence;

// Reconnaît les erreurs de PostgreSQL qui ont un sens pour l'application. Toute autre erreur reste une panne.
public static class PostgresErrors
{
    // Nom donné par PostgreSQL à la contrainte UNIQUE de la colonne email_utilisateur.
    public const string EmailUniqueConstraint = "utilisateur_email_utilisateur_key";

    // Nom de l'index unique du script SQL : deux catégories d'un utilisateur ne portent pas le même nom, majuscules ignorées.
    public const string CategoryNameUniqueIndex = "ux_categorie_nom";

    // Nom de la contrainte du script SQL : une opération n'est gardée qu'une fois pour un même compte.
    public const string TransactionExternalIdUniqueConstraint = "ux_transaction_externe";

    // Noms des index uniques du script SQL : une suggestion par catégorie dépassée et par mois, et une par mois pour chacune
    // des deux autres règles.
    public const string SuggestionCategoryMonthUniqueIndex = "ux_suggestion_categorie_mois";
    public const string SuggestionRuleMonthUniqueIndex = "ux_suggestion_regle_mois";

    // Nom donné par PostgreSQL à la contrainte UNIQUE de mouvement_sac.id_suggestion : une suggestion crée un seul mouvement.
    // EF Core écrit les tables dans l'ordre de leur nom : le mouvement part avant la ligne de l'utilisateur. Un second clic
    // sur « accepter » bute donc sur cette contrainte avant le contrôle de la ligne : c'est le même conflit, à rejouer.
    public const string MovementSuggestionUniqueConstraint = "mouvement_sac_id_suggestion_key";

    // Noms des index uniques du script SQL : une alerte ne part qu'une fois (DD §9.3). Deux vérifications simultanées
    // peuvent préparer la même : la seconde bute sur l'index, relit, et trouve l'alerte déjà partie.
    public const string BudgetAlertUniqueIndex = "ux_alerte_budget";
    public const string SuggestionAlertUniqueIndex = "ux_alerte_suggestion";
    public const string BagAlertUniqueIndex = "ux_alerte_sac";
    public const string AuthorizationAlertUniqueIndex = "ux_alerte_autorisation";

    // Nom donné par PostgreSQL à la clé primaire de parametrer : de deux premiers choix simultanés pour le même type d'alerte,
    // le second bute sur elle, relit, et change le premier.
    public const string AlertPreferencePrimaryKey = "parametrer_pkey";

    // Noms donnés par PostgreSQL aux clés étrangères vers une ligne qu'une autre requête peut supprimer au même instant :
    // la connexion d'un nouveau compte, le compte d'une nouvelle opération, la catégorie d'une opération ou d'une règle,
    // les deux sacs d'un nouveau mouvement, et la catégorie dépassée d'une nouvelle suggestion.
    public const string BankAccountConnectionForeignKey = "compte_bancaire_id_connexion_fkey";
    public const string TransactionAccountForeignKey = "transaction_id_compte_fkey";
    public const string TransactionCategoryForeignKey = "transaction_id_categorie_fkey";
    public const string RuleCategoryForeignKey = "regle_classement_id_categorie_fkey";
    public const string MovementSourceBagForeignKey = "mouvement_sac_id_sac_depart_fkey";
    public const string MovementDestinationBagForeignKey = "mouvement_sac_id_sac_arrivee_fkey";
    public const string SuggestionCategoryForeignKey = "suggestion_id_categorie_fkey";

    // Noms donnés par PostgreSQL aux clés étrangères d'une nouvelle alerte vers la catégorie, le sac ou la connexion
    // qu'une autre requête peut supprimer au même instant.
    public const string AlertCategoryForeignKey = "alerte_id_categorie_fkey";
    public const string AlertBagForeignKey = "alerte_id_sac_fkey";
    public const string AlertConnectionForeignKey = "alerte_id_connexion_fkey";

    // Noms donnés par PostgreSQL aux clés étrangères vers le compte qu'une action de l'administrateur vise, et vers la demande
    // d'une nouvelle réponse : une autre requête peut effacer ce compte, ou la tâche d'effacement cette demande, au même instant.
    public const string SecurityEventTargetForeignKey = "evenement_securite_id_utilisateur_vise_fkey";
    public const string ResponseRequestForeignKey = "reponse_support_id_demande_fkey";

    // Noms donnés par PostgreSQL aux clés étrangères vers un compte que la tâche d'effacement peut effacer au même instant
    // (GR Suppression) : une connexion, un lien reçu par e-mail ou une action de l'administrateur lit le compte, puis écrit
    // son nouvel appareil, sa session, son événement du journal ou sa demande d'e-mail. Le passage suivant ne trouve plus le compte.
    public const string DeviceAccountForeignKey = "appareil_id_utilisateur_fkey";
    public const string SessionAccountForeignKey = "session_id_utilisateur_fkey";
    public const string SecurityEventAccountForeignKey = "evenement_securite_id_utilisateur_fkey";
    public const string EmailRequestAccountForeignKey = "demande_email_id_utilisateur_fkey";

    // Nom de la contrainte du script SQL : un mouvement garde au moins un sac. L'application ne crée jamais un mouvement
    // sans sac : cette contrainte refuse seulement deux suppressions simultanées de deux sacs reliés par un mouvement,
    // qui viderait chacune un côté de ce mouvement. La seconde suppression recommence, et le déclencheur efface alors ce mouvement.
    public const string MovementWithOneBagCheck = "ck_mouvement_un_sac";

    // Les contraintes d'unicité qu'une autre requête peut prendre au même instant : l'adresse e-mail, le nom de catégorie,
    // l'opération qu'une autre mise à jour vient d'ajouter, la suggestion qu'un autre calcul du même mois vient d'enregistrer,
    // le mouvement qu'une autre acceptation de la même suggestion vient de créer, l'alerte qu'une autre vérification vient
    // d'envoyer, ou le choix d'alerte qu'une autre requête vient de créer.
    private static readonly List<string> ConflictUniqueConstraints = new List<string>
    {
        EmailUniqueConstraint, CategoryNameUniqueIndex, TransactionExternalIdUniqueConstraint,
        SuggestionCategoryMonthUniqueIndex, SuggestionRuleMonthUniqueIndex, MovementSuggestionUniqueConstraint,
        BudgetAlertUniqueIndex, SuggestionAlertUniqueIndex, BagAlertUniqueIndex, AuthorizationAlertUniqueIndex,
        AlertPreferencePrimaryKey
    };

    private static readonly List<string> ConflictForeignKeys = new List<string>
    {
        BankAccountConnectionForeignKey, TransactionAccountForeignKey, TransactionCategoryForeignKey, RuleCategoryForeignKey,
        MovementSourceBagForeignKey, MovementDestinationBagForeignKey, SuggestionCategoryForeignKey,
        AlertCategoryForeignKey, AlertBagForeignKey, AlertConnectionForeignKey,
        SecurityEventTargetForeignKey, ResponseRequestForeignKey,
        DeviceAccountForeignKey, SessionAccountForeignKey, SecurityEventAccountForeignKey, EmailRequestAccountForeignKey
    };

    // Renvoie vrai si l'erreur vient de l'unicité de l'adresse e-mail : un autre compte utilise déjà cette adresse.
    public static bool IsEmailAlreadyUsed(DbUpdateException exception)
    {
        var postgresException = FindPostgresException(exception);

        return postgresException != null
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && postgresException.ConstraintName == EmailUniqueConstraint;
    }

    // Renvoie vrai si PostgreSQL a refusé l'écriture parce qu'une autre requête vient d'écrire au même instant :
    // une contrainte d'unicité prise, une ligne visée supprimée, un mouvement vidé par une autre suppression de sac,
    // ou un interblocage. C'est un conflit : rien n'est gardé, et le passage suivant relit tout. Toute autre erreur reste une panne.
    public static bool IsConflict(Exception exception)
    {
        var postgresException = FindPostgresException(exception);

        if (postgresException == null)
        {
            return false;
        }

        if (postgresException.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return ConflictUniqueConstraints.Contains(postgresException.ConstraintName ?? string.Empty);
        }

        if (postgresException.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return ConflictForeignKeys.Contains(postgresException.ConstraintName ?? string.Empty);
        }

        if (postgresException.SqlState == PostgresErrorCodes.CheckViolation)
        {
            return postgresException.ConstraintName == MovementWithOneBagCheck;
        }

        return postgresException.SqlState == PostgresErrorCodes.DeadlockDetected;
    }

    // Renvoie vrai si l'erreur vient de cette clé étrangère : la ligne visée n'existe pas ou plus.
    public static bool IsForeignKeyViolation(Exception exception, string constraintName)
    {
        var postgresException = FindPostgresException(exception);

        return postgresException != null
            && postgresException.SqlState == PostgresErrorCodes.ForeignKeyViolation
            && postgresException.ConstraintName == constraintName;
    }

    // Renvoie vrai si PostgreSQL a arrêté cette écriture pour débloquer deux transactions qui s'attendaient l'une l'autre.
    // Rien de l'écriture arrêtée n'est gardé : elle peut être rejouée (documentation de PostgreSQL, « Deadlocks »).
    public static bool IsDeadlock(Exception exception)
    {
        return FindPostgresException(exception)?.SqlState == PostgresErrorCodes.DeadlockDetected;
    }

    // Cherche l'erreur de PostgreSQL dans toute la chaîne. Elle arrive enveloppée de plusieurs façons : dans une
    // DbUpdateException ; aussi dans une InvalidOperationException autour, car Npgsql range l'interblocage parmi les erreurs
    // passagères ; ou seule, quand une clé étrangère différée est vérifiée à la validation de la transaction.
    private static PostgresException? FindPostgresException(Exception exception)
    {
        Exception? current = exception;

        while (current != null)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException;
            }

            current = current.InnerException;
        }

        return null;
    }
}
