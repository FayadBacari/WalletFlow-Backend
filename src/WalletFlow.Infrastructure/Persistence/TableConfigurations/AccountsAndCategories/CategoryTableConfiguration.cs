using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.AccountsAndCategories;

// Relie l'entité Category à la table categorie du script SQL.
public class CategoryTableConfiguration : IEntityTypeConfiguration<Category>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categorie");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builder.HasKey(nameof(Category.Id));
        builder.Property(nameof(Category.Id)).HasColumnName("id_categorie").ValueGeneratedNever();

        builder.Property(nameof(Category.Name)).HasColumnName("nom_categorie");
        builder.Property(nameof(Category.MonthlyBudgetInCents)).HasColumnName("budget_mensuel");
        builder.Property(nameof(Category.IsOtherCategory)).HasColumnName("est_categorie_autres");
        builder.Property(nameof(Category.UserId)).HasColumnName("id_utilisateur");

        // Une catégorie appartient à un utilisateur et disparaît avec lui (ON DELETE CASCADE).
        builder.HasOne<User>().WithMany()
            .HasForeignKey(nameof(Category.UserId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
