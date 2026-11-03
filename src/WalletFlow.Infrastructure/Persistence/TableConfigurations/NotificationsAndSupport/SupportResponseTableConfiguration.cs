using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.NotificationsAndSupport;

// Relie l'entité SupportResponse à la table reponse_support du script SQL.
public class SupportResponseTableConfiguration : IEntityTypeConfiguration<SupportResponse>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<SupportResponse> builderOfTheSupportResponseTableMapping)
    {
        builderOfTheSupportResponseTableMapping.ToTable("reponse_support");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builderOfTheSupportResponseTableMapping.HasKey(nameof(SupportResponse.Id));
        builderOfTheSupportResponseTableMapping.Property(nameof(SupportResponse.Id)).HasColumnName("id_reponse").ValueGeneratedNever();

        builderOfTheSupportResponseTableMapping.Property(nameof(SupportResponse.Content)).HasColumnName("contenu_reponse");
        builderOfTheSupportResponseTableMapping.Property(nameof(SupportResponse.SupportRequestId)).HasColumnName("id_demande");

        // Une réponse suit sa demande : elle disparaît avec elle (ON DELETE CASCADE).
        builderOfTheSupportResponseTableMapping.HasOne<SupportRequest>().WithMany()
            .HasForeignKey(nameof(SupportResponse.SupportRequestId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
