using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.DatabaseCodes;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.UsersAndSecurity;

// Relie l'entité EmailToken à la table jeton du script SQL.
public class EmailTokenTableConfiguration : IEntityTypeConfiguration<EmailToken>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<EmailToken> builderOfTheEmailTokenTableMapping)
    {
        builderOfTheEmailTokenTableMapping.ToTable("jeton");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builderOfTheEmailTokenTableMapping.HasKey(nameof(EmailToken.Id));
        builderOfTheEmailTokenTableMapping.Property(nameof(EmailToken.Id)).HasColumnName("id_jeton").ValueGeneratedNever();

        builderOfTheEmailTokenTableMapping.Property(nameof(EmailToken.Type)).HasColumnName("type_jeton")
            .HasConversion(EmailTokenTypeDatabaseCodes.ConverterBetweenEmailTokenTypeAndDatabaseCode);

        // Le type est précisé pour que la recherche d'un jeton utilise l'index unique de la colonne :
        // sans lui, EF Core envoie du texte, et PostgreSQL parcourt toute la table.
        builderOfTheEmailTokenTableMapping.Property(nameof(EmailToken.TokenHash)).HasColumnName("empreinte_jeton").HasColumnType("char(64)");

        builderOfTheEmailTokenTableMapping.Property(nameof(EmailToken.ExpiresAt)).HasColumnName("date_expiration_jeton");

        // L'usage unique est contrôlé en base : deux clics simultanés sur le même lien ne peuvent pas réussir tous les deux.
        builderOfTheEmailTokenTableMapping.Property(nameof(EmailToken.UsedAt)).HasColumnName("date_utilisation_jeton").IsConcurrencyToken();

        builderOfTheEmailTokenTableMapping.Property(nameof(EmailToken.NewEmail)).HasColumnName("nouvel_email");
        builderOfTheEmailTokenTableMapping.Property(nameof(EmailToken.UserId)).HasColumnName("id_utilisateur");

        // Un jeton appartient à un utilisateur et disparaît avec lui (ON DELETE CASCADE).
        builderOfTheEmailTokenTableMapping.HasOne<User>().WithMany()
            .HasForeignKey(nameof(EmailToken.UserId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
