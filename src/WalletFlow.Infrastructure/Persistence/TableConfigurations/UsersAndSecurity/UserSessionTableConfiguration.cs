using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.UsersAndSecurity;

// Relie l'entité UserSession à la table session du script SQL.
public class UserSessionTableConfiguration : IEntityTypeConfiguration<UserSession>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("session");

        // L'identifiant de session est tiré au hasard par l'application à chaque connexion.
        builder.HasKey(nameof(UserSession.Id));
        builder.Property(nameof(UserSession.Id)).HasColumnName("id_session").HasColumnType("char(64)").ValueGeneratedNever();

        builder.Property(nameof(UserSession.LastActivityAt)).HasColumnName("date_derniere_activite");
        builder.Property(nameof(UserSession.UserId)).HasColumnName("id_utilisateur");

        // Une session appartient à un utilisateur et disparaît avec lui (ON DELETE CASCADE).
        builder.HasOne<User>().WithMany()
            .HasForeignKey(nameof(UserSession.UserId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
