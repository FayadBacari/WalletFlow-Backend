using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.UsersAndSecurity;

// Relie l'entité Device à la table appareil du script SQL.
public class DeviceTableConfiguration : IEntityTypeConfiguration<Device>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<Device> builderOfTheDeviceTableMapping)
    {
        builderOfTheDeviceTableMapping.ToTable("appareil");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builderOfTheDeviceTableMapping.HasKey(nameof(Device.Id));
        builderOfTheDeviceTableMapping.Property(nameof(Device.Id)).HasColumnName("id_appareil").ValueGeneratedNever();

        builderOfTheDeviceTableMapping.Property(nameof(Device.DeviceToken)).HasColumnName("jeton_appareil");
        builderOfTheDeviceTableMapping.Property(nameof(Device.PushToken)).HasColumnName("jeton_push");
        builderOfTheDeviceTableMapping.Property(nameof(Device.UserId)).HasColumnName("id_utilisateur");

        // Un appareil appartient à un utilisateur et disparaît avec lui (ON DELETE CASCADE).
        builderOfTheDeviceTableMapping.HasOne<User>().WithMany()
            .HasForeignKey(nameof(Device.UserId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
