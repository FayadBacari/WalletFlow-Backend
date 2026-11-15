using WalletFlow.Application.Categories;
using WalletFlow.Application.Persistence;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie l'alerte de budget (GR Dépenses, DD §9.3) : 80 %, puis 100 %, une fois par catégorie, par mois et par seuil,
// pour le seul mois en cours à Paris. L'horloge des tests marque le 4 novembre 2026 : le mois des dépenses est novembre.
public class BudgetAlertServiceTests
{
    private static readonly DateOnly November = new DateOnly(2026, 11, 1);

    private readonly BankServices services = new BankServices();
    private readonly BankAccount account;

    // Donne à l'utilisateur des tests un compte bancaire, et à sa catégorie « Loisirs » un budget de 100,00 €.
    public BudgetAlertServiceTests()
    {
        account = AddAccount(services.UserId);
        services.CategoryNamed("Loisirs").ReplaceMonthlyBudget(10_000);
    }

    // Garde un compte bancaire de cet utilisateur, dans sa propre connexion.
    private BankAccount AddAccount(Guid userId)
    {
        var connection = new BankConnection
        {
            ExternalConnectionId = "session-" + Guid.NewGuid(),
            BankName = "Banque de test",
            AuthorizationExpiresAt = services.Now.AddDays(100),
            UserId = userId
        };
        var newAccount = new BankAccount
        {
            ExternalAccountId = "compte-" + Guid.NewGuid(),
            Name = "Compte courant",
            BalanceInCents = 100_000,
            BalanceUpdatedAt = services.Now,
            BankConnectionId = connection.Id
        };
        services.Repository.SavedConnections.Add(connection);
        services.Repository.SavedAccounts.Add(newAccount);

        return newAccount;
    }

    // Garde une dépense de ce montant, en centimes, dans « Loisirs », datée du 3 novembre si le test ne donne pas de date.
    private BankTransaction Spend(int amountInCents, DateOnly? date = null, bool isInternalTransfer = false)
    {
        return services.SaveTransaction(account, "Loisirs", date ?? new DateOnly(2026, 11, 3), -amountInCents, isInternalTransfer);
    }

    private async Task<int> CheckAsync()
    {
        return await services.CreateBudgetAlerts().CheckAsync(services.UserId);
    }

    // Les alertes gardées pour l'utilisateur, triées par seuil.
    private List<Alert> SavedAlerts()
    {
        return services.Alerts.AlertsOf(services.UserId).OrderBy(alert => alert.Threshold).ToList();
    }

    // Les e-mails d'alerte mis dans la file.
    private List<EmailRequest> QueuedAlertEmails()
    {
        return services.EmailRequests.SavedRequests.Where(request => request.Type == EmailRequestType.Alert).ToList();
    }

    [Fact]
    public async Task SpendingWithoutBudgetSendsNothing()
    {
        services.SaveTransaction(account, "Alimentation", new DateOnly(2026, 11, 3), -500_000);

        var emailCount = await CheckAsync();

        Assert.Equal(0, emailCount);
        Assert.Empty(services.Alerts.SavedAlerts);
        Assert.Equal(0, services.UnitOfWork.CommittedTransactionCount);
    }

    // DD §11 : le taux est arrondi vers le bas, 79,99 € sur 100 € ne font pas 80 %.
    [Fact]
    public async Task SpendingJustUnderEightyPercentSendsNothing()
    {
        Spend(7_999);

        await CheckAsync();

        Assert.Empty(services.Alerts.SavedAlerts);
        Assert.Empty(QueuedAlertEmails());
    }

    [Fact]
    public async Task EightyPercentSendsOneAlertWithItsEmail()
    {
        Spend(8_000);

        var emailCount = await CheckAsync();

        var alert = Assert.Single(SavedAlerts());
        var email = Assert.Single(QueuedAlertEmails());
        Assert.Equal(1, emailCount);
        Assert.Equal(AlertKind.BudgetReached, alert.Kind);
        Assert.Equal(services.CategoryNamed("Loisirs").Id, alert.CategoryId);
        Assert.Equal(November, alert.Month);
        Assert.Equal((short)80, alert.Threshold);
        Assert.Equal(services.Now, alert.CreatedAt);
        Assert.Equal(alert.Id, email.AlertId);
        Assert.Equal(services.UserId, email.UserId);
    }

    // Une seule opération qui fait passer les deux seuils n'envoie que l'e-mail des 100 % ; celui des 80 % est noté comme passé.
    [Fact]
    public async Task BothThresholdsAtOnceSendOnlyTheFullBudgetEmail()
    {
        Spend(12_000);

        var emailCount = await CheckAsync();

        var alerts = SavedAlerts();
        var email = Assert.Single(QueuedAlertEmails());
        Assert.Equal(1, emailCount);
        Assert.Equal(new List<short?> { 80, 100 }, alerts.Select(alert => alert.Threshold).ToList());
        Assert.Equal(alerts[1].Id, email.AlertId);
    }

    // Chaque enregistrement ne garde qu'une alerte : l'index unique est alors le seul verrou qu'une vérification dispute,
    // et deux vérifications simultanées ne peuvent pas s'attendre l'une l'autre en boucle. Avec deux alertes par transaction,
    // vingt vérifications sur la vraie base faisaient un interblocage par course, débloqué par PostgreSQL au bout d'une seconde.
    [Fact]
    public async Task EachSaveKeepsASingleAlert()
    {
        services.CategoryNamed("Transport").ReplaceMonthlyBudget(5_000);
        Spend(12_000);
        services.SaveTransaction(account, "Transport", new DateOnly(2026, 11, 3), -6_000);

        var emailCount = await CheckAsync();

        Assert.Equal(2, emailCount);
        Assert.Equal(4, services.Alerts.SavedAlerts.Count);
        Assert.Equal(new List<int> { 1, 1, 1, 1 }, services.Alerts.AlertsPerCommit);
    }

    // Le seuil des 80 % noté sans e-mail ne part pas plus tard, même si un remboursement fait redescendre les dépenses.
    [Fact]
    public async Task EightyPercentNotedWithTheFullBudgetNeverLeavesLater()
    {
        Spend(12_000);
        await CheckAsync();
        services.SaveTransaction(account, "Loisirs", new DateOnly(2026, 11, 4), 3_500);

        var emailCount = await CheckAsync();

        Assert.Equal(0, emailCount);
        Assert.Single(QueuedAlertEmails());
    }

    [Fact]
    public async Task FullBudgetAfterEightyPercentSendsItsOwnEmail()
    {
        Spend(8_500);
        await CheckAsync();
        Spend(1_500);

        var emailCount = await CheckAsync();

        var alerts = SavedAlerts();
        Assert.Equal(1, emailCount);
        Assert.Equal(new List<short?> { 80, 100 }, alerts.Select(alert => alert.Threshold).ToList());
        Assert.Equal(alerts.Select(alert => (Guid?)alert.Id).ToList(), QueuedAlertEmails().Select(email => email.AlertId).ToList());
    }

    // Chaque seuil ne part qu'une fois par catégorie et par mois : une nouvelle vérification n'écrit rien.
    [Fact]
    public async Task SameThresholdNeverLeavesTwice()
    {
        Spend(9_000);
        await CheckAsync();
        var committedCount = services.UnitOfWork.CommittedTransactionCount;

        var emailCount = await CheckAsync();

        Assert.Equal(0, emailCount);
        Assert.Equal(committedCount, services.UnitOfWork.CommittedTransactionCount);
        Assert.Single(services.Alerts.SavedAlerts);
    }

    // Seul le mois en cours, à Paris, déclenche une alerte : le 31 octobre est un mois passé, le 1er novembre compte.
    [Fact]
    public async Task OnlyTheSpendingOfTheCurrentMonthCounts()
    {
        Spend(50_000, new DateOnly(2026, 10, 31));
        await CheckAsync();
        Assert.Empty(services.Alerts.SavedAlerts);

        Spend(8_000, new DateOnly(2026, 11, 1));
        await CheckAsync();

        Assert.Equal(November, Assert.Single(services.Alerts.SavedAlerts).Month);
    }

    // GR Dépenses : un remboursement réduit la dépense, et un transfert ne compte ni en dépense ni en revenu.
    [Fact]
    public async Task RefundsReduceTheSpendingAndTransfersDoNotCount()
    {
        Spend(10_000);
        services.SaveTransaction(account, "Loisirs", new DateOnly(2026, 11, 3), 3_000);
        Spend(50_000, isInternalTransfer: true);

        await CheckAsync();

        Assert.Empty(services.Alerts.SavedAlerts);
    }

    // Une catégorie qui reçoit plus qu'elle ne dépense n'a dépensé que zéro : son budget n'est pas atteint.
    [Fact]
    public async Task IncomeInABudgetedCategorySendsNothing()
    {
        services.CategoryNamed("Revenus").ReplaceMonthlyBudget(1);
        services.SaveTransaction(account, "Revenus", new DateOnly(2026, 11, 3), 250_000);

        await CheckAsync();

        Assert.Empty(services.Alerts.SavedAlerts);
    }

    [Fact]
    public async Task EachCategoryHasItsOwnAlerts()
    {
        services.CategoryNamed("Transport").ReplaceMonthlyBudget(5_000);
        Spend(8_000);
        services.SaveTransaction(account, "Transport", new DateOnly(2026, 11, 3), -5_000);

        var emailCount = await CheckAsync();

        Assert.Equal(2, emailCount);
        Assert.Equal(3, services.Alerts.SavedAlerts.Count);
        Assert.Equal(2, QueuedAlertEmails().Count);
    }

    // Les dépenses d'un autre utilisateur, dans ses propres catégories, ne comptent pas.
    [Fact]
    public async Task SpendingOfAnotherUserDoesNotCount()
    {
        var otherUserId = Guid.NewGuid();
        var otherCategories = DefaultCategories.CreateForUser(otherUserId);
        services.Categories.SavedCategories.AddRange(otherCategories);
        var otherAccount = AddAccount(otherUserId);
        services.Transactions.SavedTransactions.Add(new BankTransaction
        {
            ExternalTransactionId = "autre-" + Guid.NewGuid(),
            TransactionDate = new DateOnly(2026, 11, 3),
            Label = "OPERATION DE TEST",
            AmountInCents = -500_000,
            BankAccountId = otherAccount.Id,
            CategoryId = otherCategories.Single(category => category.Name == "Loisirs").Id
        });

        await CheckAsync();

        Assert.Empty(services.Alerts.SavedAlerts);
    }

    // DD §9.3 : une alerte que l'utilisateur a désactivée n'est pas créée. Réactivée, elle part au prochain changement.
    [Fact]
    public async Task DisabledBudgetAlertIsNotCreatedUntilItIsEnabledAgain()
    {
        var budgetType = services.Alerts.SavedTypes.Single(type => type.Kind == AlertKind.BudgetReached);
        var preference = AlertPreference.Create(services.UserId, budgetType, false);
        services.Alerts.SavedPreferences.Add(preference);
        Spend(9_000);

        await CheckAsync();
        Assert.Empty(services.Alerts.SavedAlerts);

        preference.Switch(budgetType, true);
        await CheckAsync();

        Assert.Single(services.Alerts.SavedAlerts);
        Assert.Single(QueuedAlertEmails());
    }

    // Une autre vérification enregistre la même alerte au même instant : l'index unique fait échouer celle-ci,
    // qui relit, trouve l'alerte partie, et n'envoie aucun second e-mail.
    [Fact]
    public async Task SimultaneousCheckSendsNoSecondEmail()
    {
        Spend(9_000);
        var otherAlert = Alert.ForBudget(services.UserId, services.CategoryNamed("Loisirs").Id, November, 80, services.Now);
        services.Alerts.AlertSavedBeforeSave = otherAlert;

        var emailCount = await CheckAsync();

        Assert.Equal(0, emailCount);
        Assert.Equal(otherAlert, Assert.Single(services.Alerts.SavedAlerts));
        Assert.Empty(QueuedAlertEmails());
    }

    // L'alerte et son e-mail partent ensemble, ou rien : une panne de la file d'e-mails n'enregistre aucune alerte.
    [Fact]
    public async Task AlertIsNotKeptWhenItsEmailCannotBeQueued()
    {
        Spend(9_000);
        services.EmailRequests.IsDown = true;

        await Assert.ThrowsAsync<InvalidOperationException>(CheckAsync);

        Assert.Empty(services.Alerts.SavedAlerts);
    }

    // Après 10 conflits, la vérification abandonne sans rien envoyer : le prochain changement la refera.
    [Fact]
    public async Task CheckGivesUpAfterTenConflicts()
    {
        Spend(9_000);
        services.Alerts.Conflicts = DatabaseChangesSavingService.MaximumTries;

        var emailCount = await CheckAsync();

        Assert.Equal(0, emailCount);
        Assert.Empty(services.Alerts.SavedAlerts);
        Assert.Empty(QueuedAlertEmails());
    }

    // Un budget fixé sous les dépenses déjà faites ce mois-ci envoie son alerte tout de suite.
    [Fact]
    public async Task NewBudgetUnderTheMonthSpendingSendsItsAlertAtOnce()
    {
        Spend(3_000);
        var budgetService = new CategoryBudgetService(
            services.Categories,
            services.CreateBudgetAlerts(),
            new DatabaseChangesSavingService(services.UnitOfWork));

        var result = await budgetService.ReplaceAsync(services.UserId, services.CategoryNamed("Loisirs").Id, 3_000);

        Assert.IsType<CategoryBudgetResult.Changed>(result);
        Assert.Equal((short)100, SavedAlerts().Last().Threshold);
        Assert.Single(QueuedAlertEmails());
    }

    // Supprimer une catégorie range ses opérations dans « Autres » : les seuils d'« Autres » sont vérifiés tout de suite.
    [Fact]
    public async Task DeletedCategoryMovesItsSpendingIntoOtherAndChecksItsBudget()
    {
        services.CategoryNamed("Autres").ReplaceMonthlyBudget(10_000);
        Spend(9_000);
        var deletionService = new CategoryDeletionService(services.Categories, services.CreateBudgetAlerts());

        var result = await deletionService.DeleteAsync(services.UserId, services.CategoryNamed("Loisirs").Id);

        var alert = Assert.Single(services.Alerts.SavedAlerts);
        Assert.IsType<CategoryDeletionResult.Deleted>(result);
        Assert.Equal(services.CategoryNamed("Autres").Id, alert.CategoryId);
    }
}
