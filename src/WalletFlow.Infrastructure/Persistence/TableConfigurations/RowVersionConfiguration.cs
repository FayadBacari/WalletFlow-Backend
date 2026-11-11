using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations;

// Version d'une ligne que l'application lit, puis modifie ou efface : la colonne système xmin de PostgreSQL.
// PostgreSQL la change à chaque écriture de la ligne, quelle que soit la colonne touchée. Toute table l'a, sans la déclarer.
// Chaque mise à jour ou effacement vérifie qu'elle n'a pas changé depuis la lecture : sinon rien n'est écrit,
// et le passage recommence avec les nouvelles valeurs (voir DatabaseChangesSavingService).
public static class RowVersionConfiguration
{
    public const string ColumnName = "xmin";

    // Ajoute la version de la ligne à l'entité, sans propriété dans le domaine : c'est un détail de la base, pas une règle de gestion.
    public static void AddRowVersion<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.Property<uint>(ColumnName)
            .HasColumnName(ColumnName)
            .HasColumnType("xid")
            .IsRowVersion();
    }
}
