using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace WalletFlow.Infrastructure.Persistence;

// Reconnaît les erreurs de PostgreSQL qui ont un sens pour l'application. Toute autre erreur reste une panne.
public static class PostgresErrors
{
    // Nom donné par PostgreSQL à la contrainte UNIQUE de la colonne email_utilisateur.
    public const string EmailUniqueConstraint = "utilisateur_email_utilisateur_key";

    // Renvoie vrai si l'erreur vient de l'unicité de l'adresse e-mail : un autre compte utilise déjà cette adresse.
    public static bool IsEmailAlreadyUsed(DbUpdateException exception)
    {
        if (exception.InnerException is not PostgresException postgresException)
        {
            return false;
        }

        return postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && postgresException.ConstraintName == EmailUniqueConstraint;
    }
}
