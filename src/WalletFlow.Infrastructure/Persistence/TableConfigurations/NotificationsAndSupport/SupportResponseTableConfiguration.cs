using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.NotificationsAndSupport;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.NotificationsAndSupport;

// Relie l'entité SupportResponse à la table reponse_support du script SQL.
public class SupportResponseTableConfiguration : IEntityTypeConfiguration<SupportResponse>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<SupportResponse> builder)
    {
        builder.ToTable("reponse_support");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builder.HasKey(nameof(SupportResponse.Id));
        builder.Property(nameof(SupportResponse.Id)).HasColumnName("id_reponse").ValueGeneratedNever();

        builder.Property(nameof(SupportResponse.Content)).HasColumnName("contenu_reponse");
        builder.Property(nameof(SupportResponse.SupportRequestId)).HasColumnName("id_demande");

        // Une réponse suit sa demande : elle disparaît avec elle (ON DELETE CASCADE).
        builder.HasOne<SupportRequest>().WithMany()
            .HasForeignKey(nameof(SupportResponse.SupportRequestId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
