using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.AccountsAndCategories;

// Relie l'entité Category à la table categorie du script SQL.
public class CategoryTableConfiguration : IEntityTypeConfiguration<Category>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<Category> builderOfTheCategoryTableMapping)
    {
        builderOfTheCategoryTableMapping.ToTable("categorie");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builderOfTheCategoryTableMapping.HasKey(nameof(Category.Id));
        builderOfTheCategoryTableMapping.Property(nameof(Category.Id)).HasColumnName("id_categorie").ValueGeneratedNever();

        builderOfTheCategoryTableMapping.Property(nameof(Category.Name)).HasColumnName("nom_categorie");
        builderOfTheCategoryTableMapping.Property(nameof(Category.MonthlyBudgetInCents)).HasColumnName("budget_mensuel");
        builderOfTheCategoryTableMapping.Property(nameof(Category.IsOtherCategory)).HasColumnName("est_categorie_autres");
        builderOfTheCategoryTableMapping.Property(nameof(Category.UserId)).HasColumnName("id_utilisateur");

        // Une catégorie appartient à un utilisateur et disparaît avec lui (ON DELETE CASCADE).
        builderOfTheCategoryTableMapping.HasOne<User>().WithMany()
            .HasForeignKey(nameof(Category.UserId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
