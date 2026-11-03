using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.AccountsAndCategories;

// Relie l'entité BankTransaction à la table transaction du script SQL.
public class BankTransactionTableConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<BankTransaction> builderOfTheBankTransactionTableMapping)
    {
        builderOfTheBankTransactionTableMapping.ToTable("transaction");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builderOfTheBankTransactionTableMapping.HasKey(nameof(BankTransaction.Id));
        builderOfTheBankTransactionTableMapping.Property(nameof(BankTransaction.Id)).HasColumnName("id_transaction").ValueGeneratedNever();

        builderOfTheBankTransactionTableMapping.Property(nameof(BankTransaction.ExternalTransactionId)).HasColumnName("id_transaction_externe");
        builderOfTheBankTransactionTableMapping.Property(nameof(BankTransaction.TransactionDate)).HasColumnName("date_transaction");
        builderOfTheBankTransactionTableMapping.Property(nameof(BankTransaction.Label)).HasColumnName("libelle_transaction");
        builderOfTheBankTransactionTableMapping.Property(nameof(BankTransaction.AmountInCents)).HasColumnName("montant_transaction");
        builderOfTheBankTransactionTableMapping.Property(nameof(BankTransaction.IsInternalTransfer)).HasColumnName("est_transfert_interne");
        builderOfTheBankTransactionTableMapping.Property(nameof(BankTransaction.BankAccountId)).HasColumnName("id_compte");
        builderOfTheBankTransactionTableMapping.Property(nameof(BankTransaction.CategoryId)).HasColumnName("id_categorie");

        // Une transaction appartient à un compte et disparaît avec lui (ON DELETE CASCADE).
        builderOfTheBankTransactionTableMapping.HasOne<BankAccount>().WithMany()
            .HasForeignKey(nameof(BankTransaction.BankAccountId))
            .OnDelete(DeleteBehavior.Cascade);

        // Une catégorie ne se supprime qu'après avoir déplacé ses transactions dans « Autres » :
        // la base vérifie ce lien à la fin de la transaction SQL (NO ACTION, différée).
        builderOfTheBankTransactionTableMapping.HasOne<Category>().WithMany()
            .HasForeignKey(nameof(BankTransaction.CategoryId))
            .OnDelete(DeleteBehavior.NoAction);
    }
}
