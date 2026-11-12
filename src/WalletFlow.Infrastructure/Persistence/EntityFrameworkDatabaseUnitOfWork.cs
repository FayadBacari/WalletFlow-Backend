using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Persistence;

namespace WalletFlow.Infrastructure.Persistence;

// L'unité de travail de la requête en cours, avec EF Core. EF Core suit les objets lus par les dépôts :
// SaveChangesAsync enregistre ce que leurs règles ont changé et ce que les dépôts ont préparé, en une seule transaction SQL, ou rien.
public class EntityFrameworkDatabaseUnitOfWork : IDatabaseUnitOfWork
{
    private readonly WalletFlowDatabaseContext databaseContext;

    // Reçoit l'accès à la base de la requête en cours, le même que celui des dépôts.
    public EntityFrameworkDatabaseUnitOfWork(WalletFlowDatabaseContext databaseContext)
    {
        this.databaseContext = databaseContext;
    }

    // Enregistre tout. La version de chaque ligne (xmin, voir RowVersionConfiguration) fait échouer l'écriture si une autre requête
    // a changé ou effacé la ligne depuis sa lecture : EF Core annule alors toute la transaction,
    // et les objets sont oubliés pour pouvoir tout relire.
    public async Task<bool> TrySaveChangesAsync()
    {
        try
        {
            await databaseContext.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            databaseContext.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateException exception)
        {
            // Une autre requête vient de donner à un compte l'adresse que ces changements donnent au leur : c'est aussi un conflit.
            // Tout est oublié, et le passage suivant voit que l'adresse est prise. Toute autre erreur reste une panne.
            if (!PostgresErrors.IsEmailAlreadyUsed(exception))
            {
                throw;
            }

            databaseContext.ChangeTracker.Clear();
            return false;
        }
    }
}
