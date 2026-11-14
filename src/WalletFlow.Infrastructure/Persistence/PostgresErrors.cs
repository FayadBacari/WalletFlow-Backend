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

    // Noms donnés par PostgreSQL aux clés étrangères vers une ligne qu'une autre requête peut supprimer au même instant :
    // la connexion d'un nouveau compte, le compte d'une nouvelle opération, la catégorie d'une opération ou d'une règle,
    // et les deux sacs d'un nouveau mouvement.
    public const string BankAccountConnectionForeignKey = "compte_bancaire_id_connexion_fkey";
    public const string TransactionAccountForeignKey = "transaction_id_compte_fkey";
    public const string TransactionCategoryForeignKey = "transaction_id_categorie_fkey";
    public const string RuleCategoryForeignKey = "regle_classement_id_categorie_fkey";
    public const string MovementSourceBagForeignKey = "mouvement_sac_id_sac_depart_fkey";
    public const string MovementDestinationBagForeignKey = "mouvement_sac_id_sac_arrivee_fkey";

    // Nom de la contrainte du script SQL : un mouvement garde au moins un sac. L'application ne crée jamais un mouvement
    // sans sac : cette contrainte refuse seulement deux suppressions simultanées de deux sacs reliés par un mouvement,
    // qui viderait chacune un côté de ce mouvement. La seconde suppression recommence, et le déclencheur efface alors ce mouvement.
    public const string MovementWithOneBagCheck = "ck_mouvement_un_sac";

    // Les contraintes d'unicité qu'une autre requête peut prendre au même instant : l'adresse e-mail, le nom de catégorie,
    // ou l'opération qu'une autre mise à jour vient d'ajouter.
    private static readonly List<string> ConflictUniqueConstraints = new List<string>
    {
        EmailUniqueConstraint, CategoryNameUniqueIndex, TransactionExternalIdUniqueConstraint
    };

    private static readonly List<string> ConflictForeignKeys = new List<string>
    {
        BankAccountConnectionForeignKey, TransactionAccountForeignKey, TransactionCategoryForeignKey, RuleCategoryForeignKey,
        MovementSourceBagForeignKey, MovementDestinationBagForeignKey
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
