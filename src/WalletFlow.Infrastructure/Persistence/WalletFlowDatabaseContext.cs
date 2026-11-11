using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence.TableConfigurations.AccountsAndCategories;
using WalletFlow.Infrastructure.Persistence.TableConfigurations.BagsAndSuggestions;
using WalletFlow.Infrastructure.Persistence.TableConfigurations.NotificationsAndSupport;
using WalletFlow.Infrastructure.Persistence.TableConfigurations.UsersAndSecurity;

namespace WalletFlow.Infrastructure.Persistence;

// Point d'accès d'EF Core à la base PostgreSQL de WalletFlow.
// Les tables sont créées par le script SQL (database/), jamais par EF Core.
public class WalletFlowDatabaseContext : DbContext
{
    // Reçoit les options (fournisseur PostgreSQL, chaîne de connexion) préparées au démarrage.
    public WalletFlowDatabaseContext(DbContextOptions<WalletFlowDatabaseContext> options)
        : base(options)
    {
    }

    // Utilisateur et sécurité du compte
    public DbSet<User> Users => Set<User>();
    public DbSet<BackupCode> BackupCodes => Set<BackupCode>();
    public DbSet<EmailToken> EmailTokens => Set<EmailToken>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();

    // Comptes, transactions et catégories
    public DbSet<BankConnection> BankConnections => Set<BankConnection>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategorizationRule> CategorizationRules => Set<CategorizationRule>();

    // Sacs et suggestions
    public DbSet<Bag> Bags => Set<Bag>();
    public DbSet<BagMovement> BagMovements => Set<BagMovement>();
    public DbSet<SavingsSuggestion> SavingsSuggestions => Set<SavingsSuggestion>();

    // Notifications et support
    public DbSet<AlertType> AlertTypes => Set<AlertType>();
    public DbSet<AlertPreference> AlertPreferences => Set<AlertPreference>();
    public DbSet<SupportRequest> SupportRequests => Set<SupportRequest>();
    public DbSet<SupportResponse> SupportResponses => Set<SupportResponse>();

    // Une écriture immédiate (journal d'un refus, remplacement d'un lien) appelle SaveChangesAsync, qui enregistre tout ce qui attend.
    // Elle refuse donc de partir si des changements attendent l'unité de travail : ils ne partent qu'avec elle (voir IDatabaseUnitOfWork).
    public void ThrowIfChangesArePending()
    {
        if (ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException(
                "Des changements attendent l'unité de travail : une écriture immédiate les enregistrerait aussi.");
        }
    }

    // Applique la correspondance de chaque entité avec sa table (nom imposé par le framework).
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new UserTableConfiguration());
        modelBuilder.ApplyConfiguration(new BackupCodeTableConfiguration());
        modelBuilder.ApplyConfiguration(new EmailTokenTableConfiguration());
        modelBuilder.ApplyConfiguration(new DeviceTableConfiguration());
        modelBuilder.ApplyConfiguration(new UserSessionTableConfiguration());
        modelBuilder.ApplyConfiguration(new SecurityEventTableConfiguration());

        modelBuilder.ApplyConfiguration(new BankConnectionTableConfiguration());
        modelBuilder.ApplyConfiguration(new BankAccountTableConfiguration());
        modelBuilder.ApplyConfiguration(new BankTransactionTableConfiguration());
        modelBuilder.ApplyConfiguration(new CategoryTableConfiguration());
        modelBuilder.ApplyConfiguration(new CategorizationRuleTableConfiguration());

        modelBuilder.ApplyConfiguration(new BagTableConfiguration());
        modelBuilder.ApplyConfiguration(new BagMovementTableConfiguration());
        modelBuilder.ApplyConfiguration(new SavingsSuggestionTableConfiguration());

        modelBuilder.ApplyConfiguration(new AlertTypeTableConfiguration());
        modelBuilder.ApplyConfiguration(new AlertPreferenceTableConfiguration());
        modelBuilder.ApplyConfiguration(new SupportRequestTableConfiguration());
        modelBuilder.ApplyConfiguration(new SupportResponseTableConfiguration());
    }
}
