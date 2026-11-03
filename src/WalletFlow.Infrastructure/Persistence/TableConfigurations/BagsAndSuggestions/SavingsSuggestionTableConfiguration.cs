using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.DatabaseCodes;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.BagsAndSuggestions;

// Relie l'entité SavingsSuggestion à la table suggestion du script SQL.
public class SavingsSuggestionTableConfiguration : IEntityTypeConfiguration<SavingsSuggestion>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<SavingsSuggestion> builderOfTheSavingsSuggestionTableMapping)
    {
        builderOfTheSavingsSuggestionTableMapping.ToTable("suggestion");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builderOfTheSavingsSuggestionTableMapping.HasKey(nameof(SavingsSuggestion.Id));
        builderOfTheSavingsSuggestionTableMapping.Property(nameof(SavingsSuggestion.Id)).HasColumnName("id_suggestion").ValueGeneratedNever();

        builderOfTheSavingsSuggestionTableMapping.Property(nameof(SavingsSuggestion.Rule)).HasColumnName("regle_suggestion")
            .HasConversion(SuggestionRuleDatabaseCodes.ConverterBetweenSuggestionRuleAndDatabaseCode);
        builderOfTheSavingsSuggestionTableMapping.Property(nameof(SavingsSuggestion.MonthlySavingsInCents)).HasColumnName("economie_mensuelle");
        builderOfTheSavingsSuggestionTableMapping.Property(nameof(SavingsSuggestion.Status)).HasColumnName("statut_suggestion")
            .HasConversion(SuggestionStatusDatabaseCodes.ConverterBetweenSuggestionStatusAndDatabaseCode);
        builderOfTheSavingsSuggestionTableMapping.Property(nameof(SavingsSuggestion.StatusChangedAt)).HasColumnName("date_statut_suggestion");
        builderOfTheSavingsSuggestionTableMapping.Property(nameof(SavingsSuggestion.UserId)).HasColumnName("id_utilisateur");
        builderOfTheSavingsSuggestionTableMapping.Property(nameof(SavingsSuggestion.CategoryId)).HasColumnName("id_categorie");

        // Une suggestion appartient à un utilisateur et disparaît avec lui (ON DELETE CASCADE).
        builderOfTheSavingsSuggestionTableMapping.HasOne<User>().WithMany()
            .HasForeignKey(nameof(SavingsSuggestion.UserId))
            .OnDelete(DeleteBehavior.Cascade);

        // Si la catégorie visée est supprimée, la suggestion reste, pour que les économies
        // déjà réalisées restent comptées (ON DELETE SET NULL).
        builderOfTheSavingsSuggestionTableMapping.HasOne<Category>().WithMany()
            .HasForeignKey(nameof(SavingsSuggestion.CategoryId))
            .OnDelete(DeleteBehavior.SetNull);
    }
}
