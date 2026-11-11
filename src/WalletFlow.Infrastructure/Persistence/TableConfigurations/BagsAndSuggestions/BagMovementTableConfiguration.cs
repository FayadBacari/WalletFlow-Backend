using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.BagsAndSuggestions;

// Relie l'entité BagMovement à la table mouvement_sac du script SQL.
public class BagMovementTableConfiguration : IEntityTypeConfiguration<BagMovement>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<BagMovement> builder)
    {
        builder.ToTable("mouvement_sac");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builder.HasKey(nameof(BagMovement.Id));
        builder.Property(nameof(BagMovement.Id)).HasColumnName("id_mouvement").ValueGeneratedNever();

        builder.Property(nameof(BagMovement.AmountInCents)).HasColumnName("montant_mouvement");
        builder.Property(nameof(BagMovement.MovedAt)).HasColumnName("date_mouvement");
        builder.Property(nameof(BagMovement.SourceBagId)).HasColumnName("id_sac_depart");
        builder.Property(nameof(BagMovement.DestinationBagId)).HasColumnName("id_sac_arrivee");
        builder.Property(nameof(BagMovement.SavingsSuggestionId)).HasColumnName("id_suggestion");

        // Quand un sac est supprimé, le mouvement garde l'autre sac (ON DELETE SET NULL).
        // Les mouvements avec l'argent disponible sont supprimés par le déclencheur
        // tg_sac_avant_suppression du script SQL.
        // C'est la base qui fait tout : si EF Core vidait lui-même la clé d'un mouvement chargé en mémoire,
        // la contrainte ck_mouvement_un_sac refuserait la suppression avant que le déclencheur n'agisse.
        builder.HasOne<Bag>().WithMany()
            .HasForeignKey(nameof(BagMovement.SourceBagId))
            .OnDelete(DeleteBehavior.ClientNoAction);
        builder.HasOne<Bag>().WithMany()
            .HasForeignKey(nameof(BagMovement.DestinationBagId))
            .OnDelete(DeleteBehavior.ClientNoAction);

        // Une suggestion acceptée crée au plus un mouvement. Si elle disparaît,
        // le mouvement reste dans l'historique du sac (ON DELETE SET NULL).
        builder.HasOne<SavingsSuggestion>().WithOne()
            .HasForeignKey<BagMovement>(nameof(BagMovement.SavingsSuggestionId))
            .OnDelete(DeleteBehavior.SetNull);
    }
}
