using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.BagsAndSuggestions;

// Relie l'entité Bag à la table sac du script SQL.
public class BagTableConfiguration : IEntityTypeConfiguration<Bag>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<Bag> builderOfTheBagTableMapping)
    {
        builderOfTheBagTableMapping.ToTable("sac");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builderOfTheBagTableMapping.HasKey(nameof(Bag.Id));
        builderOfTheBagTableMapping.Property(nameof(Bag.Id)).HasColumnName("id_sac").ValueGeneratedNever();

        builderOfTheBagTableMapping.Property(nameof(Bag.Name)).HasColumnName("nom_sac");
        builderOfTheBagTableMapping.Property(nameof(Bag.TargetAmountInCents)).HasColumnName("montant_objectif");
        builderOfTheBagTableMapping.Property(nameof(Bag.TargetDate)).HasColumnName("date_objectif");
        builderOfTheBagTableMapping.Property(nameof(Bag.UserId)).HasColumnName("id_utilisateur");

        // Un sac appartient à un utilisateur et disparaît avec lui (ON DELETE CASCADE).
        builderOfTheBagTableMapping.HasOne<User>().WithMany()
            .HasForeignKey(nameof(Bag.UserId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
