using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.DatabaseCodes;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.UsersAndSecurity;

// Relie l'entité EmailToken à la table jeton du script SQL.
public class EmailTokenTableConfiguration : IEntityTypeConfiguration<EmailToken>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<EmailToken> builder)
    {
        builder.ToTable("jeton");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builder.HasKey(nameof(EmailToken.Id));
        builder.Property(nameof(EmailToken.Id)).HasColumnName("id_jeton").ValueGeneratedNever();

        builder.Property(nameof(EmailToken.Type)).HasColumnName("type_jeton")
            .HasConversion(EmailTokenTypeDatabaseCodes.Table.Converter);

        // Le type est précisé pour que la recherche d'un jeton utilise l'index unique de la colonne :
        // sans lui, EF Core envoie du texte, et PostgreSQL parcourt toute la table.
        builder.Property(nameof(EmailToken.TokenHash)).HasColumnName("empreinte_jeton").HasColumnType("char(64)");

        builder.Property(nameof(EmailToken.ExpiresAt)).HasColumnName("date_expiration_jeton");

        builder.Property(nameof(EmailToken.UsedAt)).HasColumnName("date_utilisation_jeton");

        // L'usage unique est contrôlé en base : deux clics simultanés sur le même lien ne peuvent pas réussir tous les deux,
        // et un lien utilisé ou remplacé depuis sa lecture n'est ni utilisé ni effacé.
        RowVersionConfiguration.AddRowVersion(builder);

        builder.Property(nameof(EmailToken.NewEmail)).HasColumnName("nouvel_email");
        builder.Property(nameof(EmailToken.UserId)).HasColumnName("id_utilisateur");

        // Un jeton appartient à un utilisateur et disparaît avec lui (ON DELETE CASCADE).
        builder.HasOne<User>().WithMany()
            .HasForeignKey(nameof(EmailToken.UserId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
