using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.AccountsAndCategories;

// Relie l'entité CategorizationRule à la table regle_classement du script SQL.
public class CategorizationRuleTableConfiguration : IEntityTypeConfiguration<CategorizationRule>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<CategorizationRule> builderOfTheCategorizationRuleTableMapping)
    {
        builderOfTheCategorizationRuleTableMapping.ToTable("regle_classement");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builderOfTheCategorizationRuleTableMapping.HasKey(nameof(CategorizationRule.Id));
        builderOfTheCategorizationRuleTableMapping.Property(nameof(CategorizationRule.Id)).HasColumnName("id_regle_classement").ValueGeneratedNever();

        builderOfTheCategorizationRuleTableMapping.Property(nameof(CategorizationRule.Label)).HasColumnName("libelle_regle");
        builderOfTheCategorizationRuleTableMapping.Property(nameof(CategorizationRule.CategoryId)).HasColumnName("id_categorie");

        // Une règle n'a plus d'objet sans sa catégorie : elle disparaît avec elle (ON DELETE CASCADE).
        builderOfTheCategorizationRuleTableMapping.HasOne<Category>().WithMany()
            .HasForeignKey(nameof(CategorizationRule.CategoryId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
