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
