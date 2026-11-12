using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.AccountsAndCategories;

// Relie l'entité BankAccount à la table compte_bancaire du script SQL.
public class BankAccountTableConfiguration : IEntityTypeConfiguration<BankAccount>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.ToTable("compte_bancaire");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builder.HasKey(nameof(BankAccount.Id));
        builder.Property(nameof(BankAccount.Id)).HasColumnName("id_compte").ValueGeneratedNever();

        builder.Property(nameof(BankAccount.ExternalAccountId)).HasColumnName("id_compte_externe");
        builder.Property(nameof(BankAccount.Name)).HasColumnName("nom_compte");
        builder.Property(nameof(BankAccount.BalanceInCents)).HasColumnName("solde_compte");
        builder.Property(nameof(BankAccount.BalanceUpdatedAt)).HasColumnName("date_solde");
        builder.Property(nameof(BankAccount.BankConnectionId)).HasColumnName("id_connexion");

        // Un compte vient d'une connexion bancaire, et disparaît quand l'utilisateur
        // déconnecte cette banque (ON DELETE CASCADE).
        builder.HasOne<BankConnection>().WithMany()
            .HasForeignKey(nameof(BankAccount.BankConnectionId))
            .OnDelete(DeleteBehavior.Cascade);

        // Deux mises à jour simultanées du même solde : la seconde relit le compte avant d'écrire (voir IDatabaseUnitOfWork).
        RowVersionConfiguration.AddRowVersion(builder);
    }
}
