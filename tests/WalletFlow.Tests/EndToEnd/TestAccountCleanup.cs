using Microsoft.EntityFrameworkCore;
using WalletFlow.Tests.Infrastructure;

namespace WalletFlow.Tests.EndToEnd;

// Efface les comptes que les tests ont écrits pour de vrai dans la base de test.
public static class TestAccountCleanup
{
    // Donne une adresse qu'aucun autre test n'utilise : chaque passage crée un nouveau compte.
    public static string CreateUniqueEmail(string prefix)
    {
        return prefix + "-" + Guid.NewGuid().ToString("N") + "@exemple.test";
    }

    // Efface le compte de cette adresse. Ses catégories, liens, sessions, appareils et codes partent avec lui (ON DELETE CASCADE).
    // Ses événements du journal restent, sans identifiant ni adresse IP, comme ceux d'un compte supprimé :
    // l'API n'a pas le droit d'effacer le journal (MPD).
    public static async Task DeleteAccountAsync(string email)
    {
        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();

        await databaseContext.Users
            .Where(row => row.Email == email)
            .ExecuteDeleteAsync();
    }

    // Efface les e-mails encore en file vers cette adresse sans viser de compte : l'e-mail d'effacement survit au compte effacé
    // jusqu'à son envoi (SS §9), et un test qui échoue avant cet envoi le laisserait dans la file.
    public static async Task DeleteQueuedEmailsToAsync(string email)
    {
        await using var databaseContext = LocalTestDatabase.CreateDatabaseContext();

        await databaseContext.EmailRequests
            .Where(row => row.EmailAddress == email)
            .ExecuteDeleteAsync();
    }
}
