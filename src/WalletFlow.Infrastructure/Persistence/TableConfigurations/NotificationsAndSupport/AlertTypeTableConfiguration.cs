using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.NotificationsAndSupport;

// Relie l'entité AlertType à la table type_alerte du script SQL.
public class AlertTypeTableConfiguration : IEntityTypeConfiguration<AlertType>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<AlertType> builderOfTheAlertTypeTableMapping)
    {
        builderOfTheAlertTypeTableMapping.ToTable("type_alerte");

        // Les 8 types d'alerte sont insérés par le script SQL : leur code sert d'identifiant.
        builderOfTheAlertTypeTableMapping.HasKey(nameof(AlertType.Code));
        builderOfTheAlertTypeTableMapping.Property(nameof(AlertType.Code)).HasColumnName("code_type_alerte").ValueGeneratedNever();

        builderOfTheAlertTypeTableMapping.Property(nameof(AlertType.Label)).HasColumnName("libelle_type_alerte");
        builderOfTheAlertTypeTableMapping.Property(nameof(AlertType.CanBeDisabled)).HasColumnName("est_desactivable");
    }
}
