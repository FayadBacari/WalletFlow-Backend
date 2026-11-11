using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.AccountsAndCategories;

// Relie l'entité CategorizationRule à la table regle_classement du script SQL.
public class CategorizationRuleTableConfiguration : IEntityTypeConfiguration<CategorizationRule>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<CategorizationRule> builder)
    {
        builder.ToTable("regle_classement");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builder.HasKey(nameof(CategorizationRule.Id));
        builder.Property(nameof(CategorizationRule.Id)).HasColumnName("id_regle_classement").ValueGeneratedNever();

        builder.Property(nameof(CategorizationRule.Label)).HasColumnName("libelle_regle");
        builder.Property(nameof(CategorizationRule.CategoryId)).HasColumnName("id_categorie");

        // Une règle n'a plus d'objet sans sa catégorie : elle disparaît avec elle (ON DELETE CASCADE).
        builder.HasOne<Category>().WithMany()
            .HasForeignKey(nameof(CategorizationRule.CategoryId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
