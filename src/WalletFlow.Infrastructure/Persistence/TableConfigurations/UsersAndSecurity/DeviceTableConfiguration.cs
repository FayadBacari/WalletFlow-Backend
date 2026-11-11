using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.UsersAndSecurity;

// Relie l'entité Device à la table appareil du script SQL.
public class DeviceTableConfiguration : IEntityTypeConfiguration<Device>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("appareil");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builder.HasKey(nameof(Device.Id));
        builder.Property(nameof(Device.Id)).HasColumnName("id_appareil").ValueGeneratedNever();

        builder.Property(nameof(Device.DeviceToken)).HasColumnName("jeton_appareil").HasColumnType("char(64)");
        builder.Property(nameof(Device.PushToken)).HasColumnName("jeton_push");
        builder.Property(nameof(Device.UserId)).HasColumnName("id_utilisateur");

        // Un appareil appartient à un utilisateur et disparaît avec lui (ON DELETE CASCADE).
        builder.HasOne<User>().WithMany()
            .HasForeignKey(nameof(Device.UserId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
