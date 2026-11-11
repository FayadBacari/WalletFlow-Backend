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
    public void Configure(EntityTypeBuilder<SavingsSuggestion> builder)
    {
        builder.ToTable("suggestion");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builder.HasKey(nameof(SavingsSuggestion.Id));
        builder.Property(nameof(SavingsSuggestion.Id)).HasColumnName("id_suggestion").ValueGeneratedNever();

        builder.Property(nameof(SavingsSuggestion.Rule)).HasColumnName("regle_suggestion")
            .HasConversion(SuggestionRuleDatabaseCodes.Table.Converter);
        builder.Property(nameof(SavingsSuggestion.MonthlySavingsInCents)).HasColumnName("economie_mensuelle");
        builder.Property(nameof(SavingsSuggestion.Status)).HasColumnName("statut_suggestion")
            .HasConversion(SuggestionStatusDatabaseCodes.Table.Converter);
        builder.Property(nameof(SavingsSuggestion.StatusChangedAt)).HasColumnName("date_statut_suggestion");
        builder.Property(nameof(SavingsSuggestion.UserId)).HasColumnName("id_utilisateur");
        builder.Property(nameof(SavingsSuggestion.CategoryId)).HasColumnName("id_categorie");

        // Une suggestion appartient à un utilisateur et disparaît avec lui (ON DELETE CASCADE).
        builder.HasOne<User>().WithMany()
            .HasForeignKey(nameof(SavingsSuggestion.UserId))
            .OnDelete(DeleteBehavior.Cascade);

        // Si la catégorie visée est supprimée, la suggestion reste, pour que les économies
        // déjà réalisées restent comptées (ON DELETE SET NULL).
        builder.HasOne<Category>().WithMany()
            .HasForeignKey(nameof(SavingsSuggestion.CategoryId))
            .OnDelete(DeleteBehavior.SetNull);
    }
}
