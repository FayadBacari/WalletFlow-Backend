using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.AccountsAndCategories;

// Relie l'entité BankAccount à la table compte_bancaire du script SQL.
public class BankAccountTableConfiguration : IEntityTypeConfiguration<BankAccount>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<BankAccount> builderOfTheBankAccountTableMapping)
    {
        builderOfTheBankAccountTableMapping.ToTable("compte_bancaire");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builderOfTheBankAccountTableMapping.HasKey(nameof(BankAccount.Id));
        builderOfTheBankAccountTableMapping.Property(nameof(BankAccount.Id)).HasColumnName("id_compte").ValueGeneratedNever();

        builderOfTheBankAccountTableMapping.Property(nameof(BankAccount.ExternalAccountId)).HasColumnName("id_compte_externe");
        builderOfTheBankAccountTableMapping.Property(nameof(BankAccount.Name)).HasColumnName("nom_compte");
        builderOfTheBankAccountTableMapping.Property(nameof(BankAccount.BalanceInCents)).HasColumnName("solde_compte");
        builderOfTheBankAccountTableMapping.Property(nameof(BankAccount.BalanceUpdatedAt)).HasColumnName("date_solde");
        builderOfTheBankAccountTableMapping.Property(nameof(BankAccount.BankConnectionId)).HasColumnName("id_connexion");

        // Un compte vient d'une connexion bancaire, et disparaît quand l'utilisateur
        // déconnecte cette banque (ON DELETE CASCADE).
        builderOfTheBankAccountTableMapping.HasOne<BankConnection>().WithMany()
            .HasForeignKey(nameof(BankAccount.BankConnectionId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
