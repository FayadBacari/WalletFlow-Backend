using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.UsersAndSecurity;

// Relie l'entité UserSession à la table session du script SQL.
public class UserSessionTableConfiguration : IEntityTypeConfiguration<UserSession>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<UserSession> builderOfTheUserSessionTableMapping)
    {
        builderOfTheUserSessionTableMapping.ToTable("session");

        // L'identifiant de session est tiré au hasard par l'application à chaque connexion.
        builderOfTheUserSessionTableMapping.HasKey(nameof(UserSession.Id));
        builderOfTheUserSessionTableMapping.Property(nameof(UserSession.Id)).HasColumnName("id_session").ValueGeneratedNever();

        builderOfTheUserSessionTableMapping.Property(nameof(UserSession.LastActivityAt)).HasColumnName("date_derniere_activite");
        builderOfTheUserSessionTableMapping.Property(nameof(UserSession.UserId)).HasColumnName("id_utilisateur");

        // Une session appartient à un utilisateur et disparaît avec lui (ON DELETE CASCADE).
        builderOfTheUserSessionTableMapping.HasOne<User>().WithMany()
            .HasForeignKey(nameof(UserSession.UserId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
