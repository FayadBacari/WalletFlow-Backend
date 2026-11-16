using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Infrastructure.Persistence.DatabaseCodes;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.NotificationsAndSupport;

// Relie l'entité AlertType à la table type_alerte du script SQL.
public class AlertTypeTableConfiguration : IEntityTypeConfiguration<AlertType>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<AlertType> builder)
    {
        builder.ToTable("type_alerte");

        // Les 10 types d'alerte sont insérés par le script SQL : leur code sert d'identifiant.
        builder.HasKey(nameof(AlertType.Kind));
        builder.Property(nameof(AlertType.Kind)).HasColumnName("code_type_alerte")
            .HasConversion(AlertKindDatabaseCodes.Table.Converter)
            .ValueGeneratedNever();

        builder.Property(nameof(AlertType.Label)).HasColumnName("libelle_type_alerte");
        builder.Property(nameof(AlertType.CanBeDisabled)).HasColumnName("est_desactivable");
    }
}
