using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.AccountsAndCategories;

// Relie l'entité BankTransaction à la table transaction du script SQL.
public class BankTransactionTableConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.ToTable("transaction");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builder.HasKey(nameof(BankTransaction.Id));
        builder.Property(nameof(BankTransaction.Id)).HasColumnName("id_transaction").ValueGeneratedNever();

        builder.Property(nameof(BankTransaction.ExternalTransactionId)).HasColumnName("id_transaction_externe");
        builder.Property(nameof(BankTransaction.TransactionDate)).HasColumnName("date_transaction");
        builder.Property(nameof(BankTransaction.Label)).HasColumnName("libelle_transaction");
        builder.Property(nameof(BankTransaction.AmountInCents)).HasColumnName("montant_transaction");
        builder.Property(nameof(BankTransaction.IsInternalTransfer)).HasColumnName("est_transfert_interne");
        builder.Property(nameof(BankTransaction.BankAccountId)).HasColumnName("id_compte");
        builder.Property(nameof(BankTransaction.CategoryId)).HasColumnName("id_categorie");

        // Une transaction appartient à un compte et disparaît avec lui (ON DELETE CASCADE).
        builder.HasOne<BankAccount>().WithMany()
            .HasForeignKey(nameof(BankTransaction.BankAccountId))
            .OnDelete(DeleteBehavior.Cascade);

        // Une catégorie ne se supprime qu'après avoir déplacé ses transactions dans « Autres » :
        // la base vérifie ce lien à la fin de la transaction SQL (NO ACTION, différée).
        builder.HasOne<Category>().WithMany()
            .HasForeignKey(nameof(BankTransaction.CategoryId))
            .OnDelete(DeleteBehavior.NoAction);
    }
}
