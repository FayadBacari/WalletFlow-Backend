using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.DatabaseCodes;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.NotificationsAndSupport;

// Relie l'entité SupportRequest à la table demande_support du script SQL.
public class SupportRequestTableConfiguration : IEntityTypeConfiguration<SupportRequest>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<SupportRequest> builderOfTheSupportRequestTableMapping)
    {
        builderOfTheSupportRequestTableMapping.ToTable("demande_support");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builderOfTheSupportRequestTableMapping.HasKey(nameof(SupportRequest.Id));
        builderOfTheSupportRequestTableMapping.Property(nameof(SupportRequest.Id)).HasColumnName("id_demande").ValueGeneratedNever();

        builderOfTheSupportRequestTableMapping.Property(nameof(SupportRequest.Message)).HasColumnName("message_demande");
        builderOfTheSupportRequestTableMapping.Property(nameof(SupportRequest.ContactEmail)).HasColumnName("email_contact");
        builderOfTheSupportRequestTableMapping.Property(nameof(SupportRequest.Status)).HasColumnName("statut_demande")
            .HasConversion(SupportRequestStatusDatabaseCodes.ConverterBetweenSupportRequestStatusAndDatabaseCode);
        builderOfTheSupportRequestTableMapping.Property(nameof(SupportRequest.UserId)).HasColumnName("id_utilisateur");

        // La demande d'un utilisateur disparaît avec lui (ON DELETE CASCADE).
        // Celle d'un visiteur n'a pas d'utilisateur.
        builderOfTheSupportRequestTableMapping.HasOne<User>().WithMany()
            .HasForeignKey(nameof(SupportRequest.UserId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
