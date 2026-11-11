using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.BagsAndSuggestions;

// Relie l'entité Bag à la table sac du script SQL.
public class BagTableConfiguration : IEntityTypeConfiguration<Bag>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<Bag> builder)
    {
        builder.ToTable("sac");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builder.HasKey(nameof(Bag.Id));
        builder.Property(nameof(Bag.Id)).HasColumnName("id_sac").ValueGeneratedNever();

        builder.Property(nameof(Bag.Name)).HasColumnName("nom_sac");
        builder.Property(nameof(Bag.TargetAmountInCents)).HasColumnName("montant_objectif");
        builder.Property(nameof(Bag.TargetDate)).HasColumnName("date_objectif");
        builder.Property(nameof(Bag.UserId)).HasColumnName("id_utilisateur");

        // Un sac appartient à un utilisateur et disparaît avec lui (ON DELETE CASCADE).
        builder.HasOne<User>().WithMany()
            .HasForeignKey(nameof(Bag.UserId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
