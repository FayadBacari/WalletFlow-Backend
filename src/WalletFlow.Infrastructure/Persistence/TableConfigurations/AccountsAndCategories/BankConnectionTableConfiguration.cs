using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.DatabaseCodes;

namespace WalletFlow.Infrastructure.Persistence.TableConfigurations.AccountsAndCategories;

// Relie l'entité BankConnection à la table connexion_bancaire du script SQL.
public class BankConnectionTableConfiguration : IEntityTypeConfiguration<BankConnection>
{
    // Appelée par EF Core au démarrage (nom imposé par le framework).
    public void Configure(EntityTypeBuilder<BankConnection> builderOfTheBankConnectionTableMapping)
    {
        builderOfTheBankConnectionTableMapping.ToTable("connexion_bancaire");

        // L'identifiant est créé par l'entité elle-même, jamais par EF Core.
        builderOfTheBankConnectionTableMapping.HasKey(nameof(BankConnection.Id));
        builderOfTheBankConnectionTableMapping.Property(nameof(BankConnection.Id)).HasColumnName("id_connexion").ValueGeneratedNever();

        builderOfTheBankConnectionTableMapping.Property(nameof(BankConnection.ExternalConnectionId)).HasColumnName("id_connexion_externe");
        builderOfTheBankConnectionTableMapping.Property(nameof(BankConnection.BankName)).HasColumnName("nom_banque");
        builderOfTheBankConnectionTableMapping.Property(nameof(BankConnection.Status)).HasColumnName("statut_connexion")
            .HasConversion(BankConnectionStatusDatabaseCodes.ConverterBetweenBankConnectionStatusAndDatabaseCode);
        builderOfTheBankConnectionTableMapping.Property(nameof(BankConnection.AuthorizationExpiresAt)).HasColumnName("date_expiration_autorisation");
        builderOfTheBankConnectionTableMapping.Property(nameof(BankConnection.UserId)).HasColumnName("id_utilisateur");

        // Une connexion bancaire appartient à un utilisateur et disparaît avec lui (ON DELETE CASCADE).
        builderOfTheBankConnectionTableMapping.HasOne<User>().WithMany()
            .HasForeignKey(nameof(BankConnection.UserId))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
