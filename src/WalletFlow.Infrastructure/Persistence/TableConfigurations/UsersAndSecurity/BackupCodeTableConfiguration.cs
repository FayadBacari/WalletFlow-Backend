using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.UsersAndSecurity;

// Relie l'entité BackupCode à la table code_secours du script SQL.
public class BackupCodeTableConfiguration : IEntityTypeConfiguration<BackupCode>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<BackupCode> builder)
    {
        builder.ToTable("code_secours");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builder.HasKey(nameof(BackupCode.Id));
        builder.Property(nameof(BackupCode.Id)).HasColumnName("id_code_secours").ValueGeneratedNever();

        builder.Property(nameof(BackupCode.CodeHash)).HasColumnName("empreinte_code_secours");

        builder.Property(nameof(BackupCode.UsedAt)).HasColumnName("date_utilisation_code_secours");

        // Chaque écriture vérifie que le code n'a pas changé depuis sa lecture : de deux requêtes simultanées, une seule peut l'utiliser.
        RowVersionConfiguration.AddRowVersion(builder);

        builder.Property(nameof(BackupCode.UserId)).HasColumnName("id_utilisateur");

        // Un code de secours appartient à un utilisateur et disparaît avec lui (ON DELETE CASCADE).
        builder.HasOne<User>().WithMany()
            .HasForeignKey(nameof(BackupCode.UserId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
