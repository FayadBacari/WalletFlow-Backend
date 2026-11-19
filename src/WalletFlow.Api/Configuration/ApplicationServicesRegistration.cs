using WalletFlow.Api.BackgroundTasks;
using WalletFlow.Api.Filters;
using WalletFlow.Application.AccountDeletion;
using WalletFlow.Application.AccountRecovery;
using WalletFlow.Application.AccountSettings;
using WalletFlow.Application.Administration;
using WalletFlow.Application.Bags;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Categories;
using WalletFlow.Application.Charts;
using WalletFlow.Application.DataExport;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Notifications;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Security;
using WalletFlow.Application.Suggestions;
using WalletFlow.Application.Support;
using WalletFlow.Application.Transactions;
using WalletFlow.Application.TwoFactorAuthentication;
using WalletFlow.Application.UserAccounts;
using WalletFlow.Application.UserSessions;

namespace WalletFlow.Api.Configuration;

// Enregistre les cas d'utilisation de la couche Application et les tâches de fond de l'API, rangés par domaine. Chaque cas
// d'utilisation vit le temps d'une requête : il reçoit l'accès à la base de cette requête. L'ordre est celui de Program.cs
// avant ce rangement, et les tâches de fond démarrent dans cet ordre.
public static class ApplicationServicesRegistration
{
    // Ajoute tous les cas d'utilisation de la couche Application, avec les outils communs qu'ils partagent.
    public static IServiceCollection AddWalletFlowUseCases(this IServiceCollection services)
    {
        AddCommonTools(services);
        AddAccountUseCases(services);
        AddMoneyUseCases(services);
        AddAlertAndSupportUseCases(services);

        return services;
    }

    // Tâches de fond : effacent chaque jour les comptes non confirmés dont le lien a expiré (GR Auth), les sessions expirées,
    // les événements du journal de sécurité de plus d'un an (SS §11) et les demandes de support de plus d'un an (GR Admin),
    // envoient les e-mails de la file (SS §9), mettent à jour les soldes jusqu'à 4 fois par jour (GR Comptes),
    // toutes les heures, calculent les suggestions du mois (GR Sacs), préviennent 7 jours avant la fin d'une autorisation
    // bancaire (GR Comptes) et effacent les vieilles alertes (SS §7), et rechiffrent chaque jour avec la clé actuelle les
    // secrets de double authentification (SS §7).
    public static IServiceCollection AddWalletFlowBackgroundTasks(this IServiceCollection services)
    {
        // Le bilan des passages de chaque tâche, que lit le point de santé des tâches (voir HealthConfiguration).
        services.AddSingleton<BackgroundTaskHealth>();

        services.AddHostedService<UnconfirmedAccountsCleanupBackgroundService>();
        services.AddHostedService<ExpiredUserSessionsCleanupBackgroundService>();
        services.AddHostedService<SecurityJournalCleanupBackgroundService>();
        services.AddHostedService<SupportRequestsCleanupBackgroundService>();
        services.AddHostedService<EmailRequestsBackgroundService>();
        services.AddHostedService<BankBalancesBackgroundService>();
        services.AddHostedService<SuggestionsBackgroundService>();
        services.AddHostedService<AlertsBackgroundService>();
        services.AddHostedService<AccountDeletionBackgroundService>();
        services.AddHostedService<TotpSecretReEncryptionBackgroundService>();

        return services;
    }

    // Outils communs : enregistrement avec réessai, et journal de sécurité.
    private static void AddCommonTools(IServiceCollection services)
    {
        services.AddScoped<DatabaseChangesSavingService>();
        services.AddScoped<SecurityJournalService>();
        services.AddScoped<ForeignAccessJournalService>();
        services.AddScoped<SecurityJournalCleanupService>();
    }

    // Comptes : inscription, connexion et sessions, récupération et déblocage, file d'e-mails, double authentification,
    // réglages du compte, export des données et suppression (GR Auth, GR Compte, GR Suppression).
    private static void AddAccountUseCases(IServiceCollection services)
    {
        services.AddScoped<AccountRegistrationService>();
        services.AddScoped<AccountConfirmationService>();
        services.AddScoped<UnconfirmedAccountsCleanupService>();
        services.AddScoped<UserSessionOpeningService>();
        services.AddScoped<AccountLoginService>();
        services.AddScoped<UserSessionService>();
        services.AddScoped<ExpiredUserSessionsCleanupService>();
        services.AddScoped<PasswordRecoveryRequestService>();
        services.AddScoped<LinkEmailPreparation>();
        services.AddScoped<NoticeEmailPreparation>();
        services.AddScoped<AlertAndSupportEmailPreparation>();
        services.AddScoped<EmailRequestProcessingService>();
        services.AddScoped<PasswordResetService>();
        services.AddScoped<AccountUnlockService>();
        services.AddScoped<TwoFactorCodeCheckingService>();
        services.AddScoped<TotpSecretReEncryptionService>();
        services.AddScoped<TwoFactorLoginService>();
        services.AddScoped<TwoFactorActivationService>();
        services.AddScoped<TwoFactorDeactivationService>();
        services.AddScoped<AccountIdentityCheckingService>();
        services.AddScoped<ProfileService>();
        services.AddScoped<PersonalNameChangeService>();
        services.AddScoped<PasswordChangeService>();
        services.AddScoped<EmailChangeRequestService>();
        services.AddScoped<DataExportService>();
        services.AddScoped<DeletedAccountBankRevocation>();
        services.AddScoped<AccountDeletionService>();
        services.AddScoped<AccountErasureService>();
        services.AddScoped<EmailChangeConfirmationService>();
    }

    // Argent : catégories, banques, opérations, graphiques, sacs et suggestions (GR Comptes, GR Dépenses, GR Sacs).
    private static void AddMoneyUseCases(IServiceCollection services)
    {
        services.AddScoped<CategoryListService>();
        services.AddScoped<CategoryCreationService>();
        services.AddScoped<CategoryRenameService>();
        services.AddScoped<CategoryBudgetService>();
        services.AddScoped<CategoryDeletionService>();
        services.AddScoped<BankListService>();
        services.AddScoped<BankAuthorizationStartService>();
        services.AddScoped<BankFirstTransactionsReading>();
        services.AddScoped<BankConnectionFinishService>();
        services.AddScoped<BankConnectionListService>();
        services.AddScoped<BankBalanceUpdateService>();
        services.AddScoped<BankBackgroundUpdateService>();
        services.AddScoped<BankDisconnectionService>();
        services.AddScoped<BankBalanceTotalService>();
        services.AddScoped<BankTransactionArrival>();
        services.AddScoped<TransactionSearchService>();
        services.AddScoped<TransactionCategoryChangeService>();
        services.AddScoped<TransactionTransferService>();
        services.AddScoped<CategoryExpenseChartService>();
        services.AddScoped<MonthlyTotalsChartService>();
        services.AddScoped<MonthComparisonService>();
        services.AddScoped<BagListService>();
        services.AddScoped<BagCreationService>();
        services.AddScoped<BagRenameService>();
        services.AddScoped<BagGoalService>();
        services.AddScoped<BagDeletionService>();
        services.AddScoped<BagMovementChecks>();
        services.AddScoped<BagMovementService>();
        services.AddScoped<BagHistoryService>();
        services.AddScoped<SuggestionCalculationService>();
        services.AddScoped<SuggestionListService>();
        services.AddScoped<SuggestionAcceptanceService>();
        services.AddScoped<SuggestionDismissalService>();
    }

    // Alertes, support et administration, avec le lien du support qui désactive la double authentification (GR Notif, GR Admin).
    private static void AddAlertAndSupportUseCases(IServiceCollection services)
    {
        services.AddScoped<AlertPreparation>();
        services.AddScoped<BudgetAlertService>();
        services.AddScoped<BagGoalAlerts>();
        services.AddScoped<BankAuthorizationAlertService>();
        services.AddScoped<AlertCleanupService>();
        services.AddScoped<AlertSettingsService>();
        services.AddScoped<SupportRequestSendingService>();
        services.AddScoped<VisitorSupportRequestService>();
        services.AddScoped<SupportRequestReadingService>();
        services.AddScoped<SupportRequestCleanupService>();
        services.AddScoped<AdministratorAccessService>();
        services.AddScoped<AdministratorOnlyFilter>();
        services.AddScoped<AdminAccountSearchService>();
        services.AddScoped<AdminAccountUnlockService>();
        services.AddScoped<AdminTwoFactorLinkService>();
        services.AddScoped<AdminSupportService>();
        services.AddScoped<AdminStatisticsService>();
        services.AddScoped<TwoFactorSupportLinkService>();
    }
}
