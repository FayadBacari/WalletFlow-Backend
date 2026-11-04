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
    public WalletFlowDatabaseContext(DbContextOptions<WalletFlowDatabaseContext> optionsOfTheWalletFlowDatabaseContext)
        : base(optionsOfTheWalletFlowDatabaseContext)
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

    // Enregistre les changements. Renvoie faux si une autre requête vient de modifier ou d'effacer les mêmes lignes :
    // EF Core annule alors toute la transaction, et les objets sont oubliés pour pouvoir tout relire.
    public async Task<bool> TryToSaveChangesUnlessAnotherRequestChangedTheSameRowsAsync()
    {
        try
        {
            await SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            ChangeTracker.Clear();
            return false;
        }
    }

    // Applique la correspondance de chaque entité avec sa table (nom imposé par le framework).
    protected override void OnModelCreating(ModelBuilder builderOfTheCompleteDatabaseModel)
    {
        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new UserTableConfiguration());
        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new BackupCodeTableConfiguration());
        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new EmailTokenTableConfiguration());
        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new DeviceTableConfiguration());
        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new UserSessionTableConfiguration());
        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new SecurityEventTableConfiguration());

        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new BankConnectionTableConfiguration());
        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new BankAccountTableConfiguration());
        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new BankTransactionTableConfiguration());
        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new CategoryTableConfiguration());
        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new CategorizationRuleTableConfiguration());

        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new BagTableConfiguration());
        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new BagMovementTableConfiguration());
        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new SavingsSuggestionTableConfiguration());

        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new AlertTypeTableConfiguration());
        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new AlertPreferenceTableConfiguration());
        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new SupportRequestTableConfiguration());
        builderOfTheCompleteDatabaseModel.ApplyConfiguration(new SupportResponseTableConfiguration());
    }
}
