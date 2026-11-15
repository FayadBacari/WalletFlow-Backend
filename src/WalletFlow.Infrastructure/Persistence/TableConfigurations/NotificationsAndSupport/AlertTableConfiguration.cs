using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.DatabaseCodes;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.NotificationsAndSupport;

// Relie l'entité Alert à la table alerte du script SQL.
public class AlertTableConfiguration : IEntityTypeConfiguration<Alert>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("alerte");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builder.HasKey(nameof(Alert.Id));
        builder.Property(nameof(Alert.Id)).HasColumnName("id_alerte").ValueGeneratedNever();

        builder.Property(nameof(Alert.CreatedAt)).HasColumnName("date_alerte");
        builder.Property(nameof(Alert.Month)).HasColumnName("mois_alerte");
        builder.Property(nameof(Alert.Threshold)).HasColumnName("seuil_alerte");
        builder.Property(nameof(Alert.UserId)).HasColumnName("id_utilisateur");
        builder.Property(nameof(Alert.Kind)).HasColumnName("code_type_alerte")
            .HasConversion(AlertKindDatabaseCodes.Table.Converter);
        builder.Property(nameof(Alert.CategoryId)).HasColumnName("id_categorie");
        builder.Property(nameof(Alert.BagId)).HasColumnName("id_sac");
        builder.Property(nameof(Alert.ConnectionId)).HasColumnName("id_connexion");

        // Une alerte appartient à un utilisateur et disparaît avec lui (ON DELETE CASCADE).
        builder.HasOne<User>().WithMany()
            .HasForeignKey(nameof(Alert.UserId))
            .OnDelete(DeleteBehavior.Cascade);

        // Les types d'alerte forment une liste fixe, qu'on ne supprime pas (NO ACTION).
        builder.HasOne<AlertType>().WithMany()
            .HasForeignKey(nameof(Alert.Kind))
            .OnDelete(DeleteBehavior.NoAction);

        // Une alerte n'a plus d'objet sans ce qu'elle vise : elle disparaît avec sa catégorie, son sac ou sa connexion.
        builder.HasOne<Category>().WithMany()
            .HasForeignKey(nameof(Alert.CategoryId))
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Bag>().WithMany()
            .HasForeignKey(nameof(Alert.BagId))
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<BankConnection>().WithMany()
            .HasForeignKey(nameof(Alert.ConnectionId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
