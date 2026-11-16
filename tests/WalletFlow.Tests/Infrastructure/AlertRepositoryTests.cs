using Microsoft.EntityFrameworkCore;
using WalletFlow.Application.Bags;
using WalletFlow.Application.Emails;
using WalletFlow.Application.Notifications;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using WalletFlow.Infrastructure.Persistence.Repositories;
using WalletFlow.Tests.Fakes;
using static WalletFlow.Tests.Infrastructure.BankTestRows;
using static WalletFlow.Tests.Infrastructure.LocalTestDatabase;

namespace WalletFlow.Tests.Infrastructure;

// Vérifie les alertes et les choix d'alerte sur la vraie base (DD §9, MPD) : les codes des types, les index uniques
// et les clés étrangères vus comme des conflits, l'effacement en cascade, et les requêtes des tâches de fond.
// Chaque test annule sa transaction à la fin : la base reste comme avant.
public class AlertRepositoryTests
{
    private static readonly DateOnly August = new DateOnly(2026, 8, 1);
    private static readonly DateOnly September = new DateOnly(2026, 9, 1);
    private static readonly DateOnly October = new DateOnly(2026, 10, 1);
    private static readonly DateOnly November = new DateOnly(2026, 11, 1);

    // Ce que les alertes d'un compte de test visent : sa catégorie « Loisirs », un sac et une connexion bancaire.
    private sealed record AlertTargets(User Account, Category Category, Bag Bag, BankConnection Connection);

    // Enregistre un compte avec ses catégories, un sac et une connexion, puis oublie les objets gardés en mémoire.
    private static async Task<AlertTargets> SaveTargetsAsync(WalletFlowDatabaseContext databaseContext, string email)
    {
        var account = await SaveAccountAsync(databaseContext, email);
        var category = await databaseContext.Categories.AsNoTracking()
            .SingleAsync(saved => saved.UserId == account.Id && saved.Name == "Loisirs");
        var bag = new Bag { Name = "Vacances", UserId = account.Id };
        databaseContext.Bags.Add(bag);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();
        var (connection, _) = await SaveConnectionAsync(databaseContext, account.Id, "stable-" + Guid.NewGuid().ToString("N"));

        return new AlertTargets(account, category, bag, connection);
    }

    // Les quatre alertes d'un compte, une de chaque type.
    private static List<Alert> CreateOneAlertOfEachKind(AlertTargets targets)
    {
        return new List<Alert>
        {
            Alert.ForBudget(targets.Account.Id, targets.Category.Id, November, 80, Now),
            Alert.ForSuggestions(targets.Account.Id, October, Now),
            Alert.ForBagGoal(targets.Account.Id, targets.Bag.Id, Now),
            Alert.ForBankAuthorization(targets.Account.Id, targets.Connection.Id, Now)
        };
    }

    // Enregistre ces alertes, chacune avec son e-mail, comme un cas d'utilisation, puis oublie les objets gardés en mémoire.
    private static async Task SaveWithEmailsAsync(WalletFlowDatabaseContext databaseContext, params Alert[] alerts)
    {
        var preparation = CreateAlertPreparation(databaseContext);

        foreach (var alert in alerts)
        {
            preparation.PrepareWithEmail(alert);
        }

        Assert.True(await TrySaveChangesAsync(databaseContext));
        databaseContext.ChangeTracker.Clear();
    }

    // Lit le code enregistré pour le type de cette alerte.
    private static async Task<string> ReadKindCodeAsync(WalletFlowDatabaseContext databaseContext, Guid alertId)
    {
        return await databaseContext.Database
            .SqlQuery<string>($"SELECT code_type_alerte AS \"Value\" FROM alerte WHERE id_alerte = {alertId}")
            .SingleAsync();
    }

    // Le type de chaque alerte a le code de la table type_alerte, que remplit le script, et seuls les quatre premiers
    // peuvent être désactivés.
    [Fact]
    public async Task TypesAreTheTenOfTheScriptWithTheirRule()
    {
        await using var databaseContext = CreateDatabaseContext();

        var types = await new AlertRepository(databaseContext).FindTypesAsync();

        Assert.Equal(Enum.GetValues<AlertKind>().ToHashSet(), types.Select(type => type.Kind).ToHashSet());
        Assert.Equal(
            new HashSet<AlertKind> { AlertKind.BudgetReached, AlertKind.NewSuggestion, AlertKind.BagGoalReached, AlertKind.BankAuthorizationToRenew },
            types.Where(type => type.CanBeDisabled).Select(type => type.Kind).ToHashSet());
        Assert.Equal("Autorisation bancaire à renouveler", types.Single(type => type.Kind == AlertKind.BankAuthorizationToRenew).Label);
    }

    // Chaque alerte est enregistrée avec son code et son e-mail, puis retrouvée par la recherche de son type,
    // et son e-mail lit le nom de ce qu'elle vise.
    [Fact]
    public async Task EachAlertIsSavedWithItsEmailAndFoundAgain()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var targets = await SaveTargetsAsync(databaseContext, "test-alertes-types@exemple.test");
        var alerts = CreateOneAlertOfEachKind(targets);
        var repository = new AlertRepository(databaseContext);

        await SaveWithEmailsAsync(databaseContext, alerts.ToArray());

        var codes = new List<string>();
        foreach (var alert in alerts)
        {
            codes.Add(await ReadKindCodeAsync(databaseContext, alert.Id));
        }

        Assert.Equal(new List<string> { "budget_atteint", "nouvelle_suggestion", "objectif_sac_atteint", "autorisation_bancaire" }, codes);
        Assert.Equal(alerts[0].Id, Assert.Single(await repository.FindBudgetAlertsAsync(targets.Account.Id, November)).Id);
        Assert.Empty(await repository.FindBudgetAlertsAsync(targets.Account.Id, October));
        Assert.Equal(alerts[2].Id, (await repository.FindBagAlertAsync(targets.Bag.Id))?.Id);
        Assert.Equal(alerts[3].Id, (await repository.FindConnectionAlertAsync(targets.Connection.Id))?.Id);

        var emails = await databaseContext.EmailRequests.AsNoTracking().Where(request => request.UserId == targets.Account.Id).ToListAsync();
        Assert.Equal(alerts.Select(alert => (Guid?)alert.Id).ToHashSet(), emails.Select(email => email.AlertId).ToHashSet());
        Assert.All(emails, email => Assert.Equal(EmailRequestType.Alert, email.Type));

        var budget = Assert.IsType<AlertEmailContent.BudgetReached>(await repository.ReadEmailContentAsync(alerts[0].Id));
        Assert.Equal(("Loisirs", November, (short)80), (budget.CategoryName, budget.Month, budget.Threshold));
        Assert.Equal(October, Assert.IsType<AlertEmailContent.NewSuggestions>(await repository.ReadEmailContentAsync(alerts[1].Id)).Month);
        Assert.Equal("Vacances", Assert.IsType<AlertEmailContent.BagGoalReached>(await repository.ReadEmailContentAsync(alerts[2].Id)).BagName);
        var authorization = Assert.IsType<AlertEmailContent.BankAuthorizationToRenew>(await repository.ReadEmailContentAsync(alerts[3].Id));
        Assert.Equal(("Banque de test", targets.Connection.AuthorizationExpiresAt), (authorization.BankName, authorization.AuthorizationExpiresAt));
        Assert.Null(await repository.ReadEmailContentAsync(Guid.NewGuid()));
    }

    // Une alerte ne part qu'une fois : la même alerte, préparée par une autre vérification, est un conflit à rejouer.
    [Fact]
    public async Task SameAlertTwiceIsAConflictToReplay()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var targets = await SaveTargetsAsync(databaseContext, "test-alertes-doubles@exemple.test");
        await SaveWithEmailsAsync(databaseContext, CreateOneAlertOfEachKind(targets).ToArray());
        var repository = new AlertRepository(databaseContext);

        foreach (var duplicate in CreateOneAlertOfEachKind(targets))
        {
            repository.PrepareAlert(duplicate);

            Assert.False(await TrySaveChangesAsync(databaseContext));
        }

        Assert.Equal(4, await databaseContext.Alerts.CountAsync(alert => alert.UserId == targets.Account.Id));
    }

    // Le seuil de 100 % du même mois, ou le même seuil d'un autre mois, est une autre alerte.
    [Fact]
    public async Task OtherThresholdOrOtherMonthIsAnotherAlert()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var targets = await SaveTargetsAsync(databaseContext, "test-alertes-seuils@exemple.test");

        await SaveWithEmailsAsync(
            databaseContext,
            Alert.ForBudget(targets.Account.Id, targets.Category.Id, November, 80, Now),
            Alert.ForBudget(targets.Account.Id, targets.Category.Id, November, 100, Now),
            Alert.ForBudget(targets.Account.Id, targets.Category.Id, October, 80, Now));

        Assert.Equal(3, await databaseContext.Alerts.CountAsync(alert => alert.UserId == targets.Account.Id));
    }

    // Une catégorie, un sac ou une connexion supprimé par une autre requête juste avant : l'alerte qui le vise est un conflit,
    // et le passage suivant ne la prépare plus.
    [Fact]
    public async Task AlertForATargetDeletedMeanwhileIsAConflict()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var targets = await SaveTargetsAsync(databaseContext, "test-alertes-cible@exemple.test");
        var repository = new AlertRepository(databaseContext);
        await databaseContext.Categories.Where(category => category.Id == targets.Category.Id).ExecuteDeleteAsync();
        await databaseContext.Bags.Where(bag => bag.Id == targets.Bag.Id).ExecuteDeleteAsync();
        await databaseContext.BankConnections.Where(connection => connection.Id == targets.Connection.Id).ExecuteDeleteAsync();

        foreach (var alert in CreateOneAlertOfEachKind(targets).Where(alert => alert.Kind != AlertKind.NewSuggestion))
        {
            repository.PrepareAlert(alert);

            Assert.False(await TrySaveChangesAsync(databaseContext));
        }
    }

    // De deux premiers choix pour le même type, le second bute sur la clé de parametrer : c'est un conflit à rejouer.
    [Fact]
    public async Task SecondFirstChoiceIsAConflictToReplay()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveConfirmedAccountAsync(databaseContext, "test-alertes-choix@exemple.test");
        var repository = new AlertRepository(databaseContext);
        var budgetType = (await repository.FindTypesAsync()).Single(type => type.Kind == AlertKind.BudgetReached);
        repository.PreparePreference(AlertPreference.Create(account.Id, budgetType, false));
        Assert.True(await TrySaveChangesAsync(databaseContext));
        databaseContext.ChangeTracker.Clear();

        repository.PreparePreference(AlertPreference.Create(account.Id, budgetType, true));

        Assert.False(await TrySaveChangesAsync(databaseContext));
        Assert.False(await repository.IsEnabledAsync(account.Id, AlertKind.BudgetReached));
    }

    // Un choix ne coupe que son type, et se change : la base relit le nouveau choix.
    [Fact]
    public async Task ChoiceDisablesOnlyItsTypeAndCanBeChanged()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveConfirmedAccountAsync(databaseContext, "test-alertes-changer@exemple.test");
        var repository = new AlertRepository(databaseContext);
        var bagType = (await repository.FindTypesAsync()).Single(type => type.Kind == AlertKind.BagGoalReached);
        repository.PreparePreference(AlertPreference.Create(account.Id, bagType, false));
        Assert.True(await TrySaveChangesAsync(databaseContext));
        databaseContext.ChangeTracker.Clear();

        Assert.False(await repository.IsEnabledAsync(account.Id, AlertKind.BagGoalReached));
        Assert.True(await repository.IsEnabledAsync(account.Id, AlertKind.BudgetReached));
        Assert.True(await repository.IsEnabledAsync(Guid.NewGuid(), AlertKind.BagGoalReached));

        var preference = await repository.FindPreferenceAsync(account.Id, AlertKind.BagGoalReached);
        Assert.NotNull(preference);
        preference.Switch(bagType, true);
        Assert.True(await TrySaveChangesAsync(databaseContext));

        Assert.True(await repository.IsEnabledAsync(account.Id, AlertKind.BagGoalReached));
        Assert.True(Assert.Single(await repository.FindPreferencesAsync(account.Id)).IsEnabled);
    }

    // GR Notif : même si le code se trompait, la base refuse de couper une alerte de sécurité (tg_parametrer_controle).
    // Ce n'est pas un conflit à rejouer : c'est une panne.
    [Fact]
    public async Task DatabaseRefusesToDisableASecurityAlert()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveConfirmedAccountAsync(databaseContext, "test-alertes-securite@exemple.test");
        new AlertRepository(databaseContext).PreparePreference(new AlertPreference { UserId = account.Id, Kind = AlertKind.NewDevice, IsEnabled = false });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => TrySaveChangesAsync(databaseContext));
    }

    // La tâche de fond prévient une connexion dont l'autorisation finit avant la limite, une seule fois,
    // et jamais celle d'un utilisateur qui a coupé cette alerte.
    [Fact]
    public async Task ConnectionsToWarnEndBeforeTheLimitWithoutAlertNorDisabledChoice()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var account = await SaveConfirmedAccountAsync(databaseContext, "test-alertes-fin-1@exemple.test");
        var otherAccount = await SaveConfirmedAccountAsync(databaseContext, "test-alertes-fin-2@exemple.test");
        var limit = Now.AddDays(7);
        var dueNow = CreateConnection(account.Id, limit);
        var later = CreateConnection(account.Id, limit.AddTicks(10));
        var alreadyWarned = CreateConnection(account.Id, Now.AddDays(2));
        var ofUserWhoDisabledIt = CreateConnection(otherAccount.Id, Now.AddDays(2));
        databaseContext.BankConnections.AddRange(dueNow, later, alreadyWarned, ofUserWhoDisabledIt);
        databaseContext.Alerts.Add(Alert.ForBankAuthorization(account.Id, alreadyWarned.Id, Now));
        databaseContext.AlertPreferences.Add(new AlertPreference
        {
            UserId = otherAccount.Id,
            Kind = AlertKind.BankAuthorizationToRenew,
            IsEnabled = false
        });
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        var connectionIds = await new AlertRepository(databaseContext).FindConnectionsToWarnAsync(limit);

        Assert.Contains(dueNow.Id, connectionIds);
        Assert.DoesNotContain(later.Id, connectionIds);
        Assert.DoesNotContain(alreadyWarned.Id, connectionIds);
        Assert.DoesNotContain(ofUserWhoDisabledIt.Id, connectionIds);
    }

    // SS §7 : les alertes de budget et de suggestion d'avant le premier mois gardé sont effacées, avec leur e-mail s'il
    // attend encore. Les autres restent, comme celles d'un sac ou d'une connexion.
    [Fact]
    public async Task OldMonthAlertsAreErasedWithTheirWaitingEmail()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var targets = await SaveTargetsAsync(databaseContext, "test-alertes-vieilles@exemple.test");
        var oldBudget = Alert.ForBudget(targets.Account.Id, targets.Category.Id, August, 80, Now);
        var oldSuggestion = Alert.ForSuggestions(targets.Account.Id, August, Now);
        var keptBudget = Alert.ForBudget(targets.Account.Id, targets.Category.Id, September, 100, Now);
        var bagAlert = Alert.ForBagGoal(targets.Account.Id, targets.Bag.Id, Now);
        var connectionAlert = Alert.ForBankAuthorization(targets.Account.Id, targets.Connection.Id, Now);
        await SaveWithEmailsAsync(databaseContext, oldBudget, oldSuggestion, keptBudget, bagAlert, connectionAlert);

        var deletedCount = await new AlertRepository(databaseContext).DeleteMonthAlertsBeforeAsync(September);

        var keptIds = await databaseContext.Alerts.Where(alert => alert.UserId == targets.Account.Id).Select(alert => alert.Id).ToListAsync();
        Assert.True(deletedCount >= 2);
        Assert.Equal(new HashSet<Guid> { keptBudget.Id, bagAlert.Id, connectionAlert.Id }, keptIds.ToHashSet());
        Assert.False(await databaseContext.EmailRequests.AnyAsync(request => request.AlertId == oldBudget.Id || request.AlertId == oldSuggestion.Id));
        Assert.Equal(3, await databaseContext.EmailRequests.CountAsync(request => request.UserId == targets.Account.Id));
    }

    // Une alerte part avec ce qu'elle vise, et son e-mail avec elle (ON DELETE CASCADE) : rien ne part plus pour
    // un sac supprimé, une catégorie supprimée ou une banque déconnectée.
    [Fact]
    public async Task AlertLeavesWithItsTargetAndItsWaitingEmail()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var targets = await SaveTargetsAsync(databaseContext, "test-alertes-cascade@exemple.test");
        await SaveWithEmailsAsync(databaseContext, CreateOneAlertOfEachKind(targets).ToArray());

        var bagRepository = new BagRepository(databaseContext);
        bagRepository.PrepareDeletion(Assert.IsType<Bag>(await bagRepository.FindAsync(targets.Account.Id, targets.Bag.Id)));
        Assert.True(await TrySaveChangesAsync(databaseContext));
        databaseContext.ChangeTracker.Clear();
        Assert.True(await new CategoryRepository(databaseContext).DeleteAsync(targets.Account.Id, targets.Category.Id));
        Assert.True(await new BankConnectionRepository(databaseContext)
            .DeleteAsync(targets.Account.Id, targets.Connection.Id, targets.Connection.ExternalConnectionId));

        var kinds = await databaseContext.Alerts.Where(alert => alert.UserId == targets.Account.Id).Select(alert => alert.Kind).ToListAsync();
        Assert.Equal(AlertKind.NewSuggestion, Assert.Single(kinds));
        Assert.Equal(1, await databaseContext.EmailRequests.CountAsync(request => request.UserId == targets.Account.Id));
    }

    // DD §9.3 : un nouveau montant efface l'alerte de l'ancien. Si le sac l'atteint déjà, une nouvelle alerte prend
    // aussitôt sa place pour le même sac, dans le même enregistrement : EF Core doit effacer l'ancienne ligne avant
    // d'écrire la nouvelle, sinon l'index ux_alerte_sac refuserait l'enregistrement à chaque essai.
    [Fact]
    public async Task NewGoalAlreadyReachedReplacesTheAlertInOneSave()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var targets = await SaveTargetsAsync(databaseContext, "test-alertes-objectif@exemple.test");
        var clock = new FakeTimeProvider();
        var bag = await databaseContext.Bags.SingleAsync(saved => saved.Id == targets.Bag.Id);
        bag.ReplaceGoal(10_000, null, UsersCalendar.FindToday(clock.GetUtcNow()));
        databaseContext.BagMovements.Add(new BagMovement { AmountInCents = 10_000, MovedAt = Now, DestinationBagId = bag.Id });
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();
        var firstAlert = Alert.ForBagGoal(targets.Account.Id, bag.Id, Now);
        await SaveWithEmailsAsync(databaseContext, firstAlert);
        var goalService = new BagGoalService(new BagRepository(databaseContext), CreateBagGoalAlerts(databaseContext), CreateSavingService(databaseContext), clock);

        var result = await goalService.ReplaceAsync(targets.Account.Id, bag.Id, 8_000, null);

        Assert.IsType<BagGoalResult.Changed>(result);
        var newAlert = await databaseContext.Alerts.AsNoTracking().SingleAsync(alert => alert.BagId == bag.Id);
        Assert.NotEqual(firstAlert.Id, newAlert.Id);
        var email = await databaseContext.EmailRequests.AsNoTracking().SingleAsync(request => request.UserId == targets.Account.Id);
        Assert.Equal(newAlert.Id, email.AlertId);
    }

    // La connexion réécrite avec sa version fait échouer l'enregistrement si une autre requête l'a écrite depuis sa lecture.
    [Fact]
    public async Task UnchangedConnectionCheckFailsAfterAnotherWrite()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var targets = await SaveTargetsAsync(databaseContext, "test-alertes-version@exemple.test");
        var repository = new BankConnectionRepository(databaseContext);
        var connection = Assert.IsType<BankConnection>(await repository.FindConnectionByIdAsync(targets.Connection.Id));
        await WriteAsAnotherRequestAsync(databaseContext, () => databaseContext.Database.ExecuteSqlRawAsync(
            "UPDATE connexion_bancaire SET date_expiration_autorisation = date_expiration_autorisation + interval '180 days' WHERE id_connexion = {0}",
            targets.Connection.Id));

        repository.PrepareUnchangedCheck(connection);

        Assert.False(await TrySaveChangesAsync(databaseContext));
    }

    // Sans autre écriture, la vérification passe et ne change rien à la connexion.
    [Fact]
    public async Task UnchangedConnectionCheckPassesWithoutChangingAnything()
    {
        await using var databaseContext = CreateDatabaseContext();
        await using var transaction = await databaseContext.Database.BeginTransactionAsync();
        var targets = await SaveTargetsAsync(databaseContext, "test-alertes-version-ok@exemple.test");
        var repository = new BankConnectionRepository(databaseContext);
        var connection = Assert.IsType<BankConnection>(await repository.FindConnectionByIdAsync(targets.Connection.Id));

        repository.PrepareUnchangedCheck(connection);

        Assert.True(await TrySaveChangesAsync(databaseContext));
        databaseContext.ChangeTracker.Clear();
        var saved = await databaseContext.BankConnections.AsNoTracking().SingleAsync(row => row.Id == targets.Connection.Id);
        Assert.Equal((connection.Status, connection.AuthorizationExpiresAt), (saved.Status, saved.AuthorizationExpiresAt));
    }

    // Crée une connexion de ce compte dont l'autorisation finit à cette date, sans l'enregistrer.
    private static BankConnection CreateConnection(Guid userId, DateTime authorizationExpiresAt)
    {
        return new BankConnection
        {
            ExternalConnectionId = "session-" + Guid.NewGuid(),
            BankName = "Banque de test",
            AuthorizationExpiresAt = authorizationExpiresAt,
            UserId = userId
        };
    }
}
