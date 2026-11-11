using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.DatabaseCodes;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.UsersAndSecurity;

// Relie l'entité SecurityEvent à la table evenement_securite du script SQL.
public class SecurityEventTableConfiguration : IEntityTypeConfiguration<SecurityEvent>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<SecurityEvent> builder)
    {
        builder.ToTable("evenement_securite");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builder.HasKey(nameof(SecurityEvent.Id));
        builder.Property(nameof(SecurityEvent.Id)).HasColumnName("id_evenement").ValueGeneratedNever();

        builder.Property(nameof(SecurityEvent.Type)).HasColumnName("type_evenement")
            .HasConversion(SecurityEventTypeDatabaseCodes.Table.Converter);
        builder.Property(nameof(SecurityEvent.Result)).HasColumnName("resultat_evenement")
            .HasConversion(SecurityEventResultDatabaseCodes.Table.Converter);
        builder.Property(nameof(SecurityEvent.OccurredAt)).HasColumnName("date_evenement");
        builder.Property(nameof(SecurityEvent.IpAddress)).HasColumnName("adresse_ip_evenement");
        builder.Property(nameof(SecurityEvent.UserId)).HasColumnName("id_utilisateur");

        // Un événement reste dans le journal quand son utilisateur est supprimé,
        // mais perd le lien vers lui (ON DELETE SET NULL).
        builder.HasOne<User>().WithMany()
            .HasForeignKey(nameof(SecurityEvent.UserId))
            .OnDelete(DeleteBehavior.SetNull);
    }
}
