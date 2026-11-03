using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.NotificationsAndSupport;

// Relie l'entité AlertPreference à la table parametrer du script SQL.
public class AlertPreferenceTableConfiguration : IEntityTypeConfiguration<AlertPreference>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<AlertPreference> builderOfTheAlertPreferenceTableMapping)
    {
        builderOfTheAlertPreferenceTableMapping.ToTable("parametrer");

        // La clé primaire réunit l'utilisateur et le type d'alerte.
        builderOfTheAlertPreferenceTableMapping.HasKey(nameof(AlertPreference.UserId), nameof(AlertPreference.AlertTypeCode));

        builderOfTheAlertPreferenceTableMapping.Property(nameof(AlertPreference.UserId)).HasColumnName("id_utilisateur");
        builderOfTheAlertPreferenceTableMapping.Property(nameof(AlertPreference.AlertTypeCode)).HasColumnName("code_type_alerte");
        builderOfTheAlertPreferenceTableMapping.Property(nameof(AlertPreference.IsEnabled)).HasColumnName("alerte_activee");

        // Un choix d'alerte appartient à un utilisateur et disparaît avec lui (ON DELETE CASCADE).
        builderOfTheAlertPreferenceTableMapping.HasOne<User>().WithMany()
            .HasForeignKey(nameof(AlertPreference.UserId))
            .OnDelete(DeleteBehavior.Cascade);

        // Les types d'alerte forment une liste fixe, qu'on ne supprime pas (NO ACTION).
        builderOfTheAlertPreferenceTableMapping.HasOne<AlertType>().WithMany()
            .HasForeignKey(nameof(AlertPreference.AlertTypeCode))
            .OnDelete(DeleteBehavior.NoAction);
    }
}
