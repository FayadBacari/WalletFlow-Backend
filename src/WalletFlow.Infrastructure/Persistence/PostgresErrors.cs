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

    // Renvoie vrai si l'erreur vient de l'unicité de l'adresse e-mail : un autre compte utilise déjà cette adresse.
    public static bool IsEmailAlreadyUsed(DbUpdateException exception)
    {
        return IsUniqueViolation(exception, EmailUniqueConstraint);
    }

    // Renvoie vrai si l'erreur vient de l'unicité du nom des catégories : l'utilisateur a déjà une catégorie de ce nom.
    public static bool IsCategoryNameAlreadyUsed(DbUpdateException exception)
    {
        return IsUniqueViolation(exception, CategoryNameUniqueIndex);
    }

    // Renvoie vrai si PostgreSQL a arrêté cette écriture pour débloquer deux transactions qui s'attendaient l'une l'autre.
    // Rien de l'écriture arrêtée n'est gardé : elle peut être rejouée (documentation de PostgreSQL, « Deadlocks »).
    // L'erreur de PostgreSQL arrive enveloppée, parfois deux fois : Npgsql range l'interblocage parmi les erreurs passagères,
    // et EF Core le donne alors dans une InvalidOperationException, autour de la DbUpdateException. Toute la chaîne est lue.
    public static bool IsDeadlock(Exception exception)
    {
        Exception? current = exception;

        while (current != null)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException.SqlState == PostgresErrorCodes.DeadlockDetected;
            }

            current = current.InnerException;
        }

        return false;
    }

    // Nom donné par PostgreSQL à la clé étrangère qui relie un compte bancaire à sa connexion.
    public const string BankAccountConnectionForeignKey = "compte_bancaire_id_connexion_fkey";

    // Renvoie vrai si un compte bancaire allait être ajouté à une connexion qu'une autre requête vient de supprimer.
    public static bool IsBankConnectionGone(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.ForeignKeyViolation
            && postgresException.ConstraintName == BankAccountConnectionForeignKey;
    }

    // Renvoie vrai si la base a refusé l'écriture à cause de cette contrainte d'unicité.
    private static bool IsUniqueViolation(DbUpdateException exception, string constraintName)
    {
        if (exception.InnerException is not PostgresException postgresException)
        {
            return false;
        }

        return postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && postgresException.ConstraintName == constraintName;
    }
}
