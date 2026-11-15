using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using static WalletFlow.Tests.Application.SuggestionServices;

namespace WalletFlow.Tests.Application;

// Vérifie l'alerte « nouvelle suggestion » (GR Sacs, DD §9.3) : une seule par mois, quand le calcul du mois propose
// au moins une suggestion, et le calcul que fait la tâche de fond. Le 5 novembre 2026, le mois évalué est octobre.
public class SuggestionAlertsTests
{
    private readonly SuggestionServices services = new SuggestionServices();

    // Les e-mails d'alerte mis dans la file.
    private List<EmailRequest> QueuedAlertEmails()
    {
        return services.EmailRequests.SavedRequests.Where(request => request.Type == EmailRequestType.Alert).ToList();
    }

    // Donne à l'utilisateur 500 € à la banque, hors de tout sac, et une opération en octobre : la règle de l'argent
    // disponible propose une suggestion.
    private void GiveUnallocatedMoney()
    {
        var account = services.AddBankAccount(50_000);
        services.SaveTransaction(account, "Alimentation", October.AddDays(4), -1_000);
    }

    [Fact]
    public async Task CalculationThatProposesASuggestionSendsOneAlertForTheMonth()
    {
        GiveUnallocatedMoney();

        await services.CreateList().ListAsync(services.UserId);

        var alert = Assert.Single(services.Alerts.AlertsOf(services.UserId));
        var email = Assert.Single(QueuedAlertEmails());
        Assert.Equal(AlertKind.NewSuggestion, alert.Kind);
        Assert.Equal(October, alert.Month);
        Assert.Equal(services.Now, alert.CreatedAt);
        Assert.Equal(alert.Id, email.AlertId);
        Assert.Single(services.SavedSuggestionsOfUser());
    }

    // Le mois n'est calculé qu'une fois : relire la liste n'envoie aucune autre alerte.
    [Fact]
    public async Task ReadingTheListAgainSendsNoOtherAlert()
    {
        GiveUnallocatedMoney();
        await services.CreateList().ListAsync(services.UserId);

        await services.CreateList().ListAsync(services.UserId);

        Assert.Single(services.Alerts.SavedAlerts);
        Assert.Single(QueuedAlertEmails());
    }

    // Un autre calcul du même mois s'enregistre juste avant celui-ci, avec son alerte : celui-ci échoue, relit,
    // trouve le mois noté, et n'enregistre ni suggestion ni seconde alerte.
    [Fact]
    public async Task SimultaneousCalculationOfTheSameMonthSendsOneAlert()
    {
        GiveUnallocatedMoney();
        var otherAlert = Alert.ForSuggestions(services.UserId, October, services.Now);
        services.Suggestions.OtherRequestBeforeSave = () =>
        {
            services.User.NoteSuggestionMonth(October);
            services.Alerts.SavedAlerts.Add(otherAlert);
        };

        await services.CreateList().ListAsync(services.UserId);

        Assert.Equal(otherAlert, Assert.Single(services.Alerts.SavedAlerts));
        Assert.Empty(QueuedAlertEmails());
        Assert.Empty(services.SavedSuggestionsOfUser());
    }

    // Tout l'argent est dans un sac et le mois n'a ni revenu ni historique : le calcul ne propose rien, et rien ne part.
    [Fact]
    public async Task CalculationThatProposesNothingSendsNoAlert()
    {
        var account = services.AddBankAccount(10_000);
        services.SaveTransaction(account, "Alimentation", October.AddDays(4), -1_000);
        services.Bags.SavedMovements.Add(new BagMovement
        {
            AmountInCents = 10_000,
            MovedAt = services.Now,
            DestinationBagId = services.SaveBag("Épargne").Id
        });

        await services.CreateList().ListAsync(services.UserId);

        Assert.Equal(October, services.User.LastSuggestionMonth);
        Assert.Empty(services.SavedSuggestionsOfUser());
        Assert.Empty(services.Alerts.SavedAlerts);
        Assert.Empty(QueuedAlertEmails());
    }

    // Une suggestion ignorée met sa règle en pause : rien n'est proposé, donc rien n'est annoncé.
    [Fact]
    public async Task PausedRuleProposesNothingAndSendsNoAlert()
    {
        GiveUnallocatedMoney();
        services.SaveSuggestion(
            SuggestionRule.UnallocatedMoney,
            October.AddMonths(-1),
            5_000,
            SuggestionStatus.Ignored,
            statusChangedAt: services.Now.AddDays(-3));

        await services.CreateList().ListAsync(services.UserId);

        Assert.Empty(services.Alerts.SavedAlerts);
    }

    // DD §9.3 : les suggestions sont calculées, mais l'alerte désactivée n'est pas créée.
    [Fact]
    public async Task DisabledSuggestionAlertIsNotCreated()
    {
        GiveUnallocatedMoney();
        var suggestionType = services.Alerts.SavedTypes.Single(type => type.Kind == AlertKind.NewSuggestion);
        services.Alerts.SavedPreferences.Add(AlertPreference.Create(services.UserId, suggestionType, false));

        await services.CreateList().ListAsync(services.UserId);

        Assert.Single(services.SavedSuggestionsOfUser());
        Assert.Empty(services.Alerts.SavedAlerts);
        Assert.Empty(QueuedAlertEmails());
    }

    // Les suggestions, le mois noté, l'alerte et son e-mail partent ensemble, ou rien.
    [Fact]
    public async Task CalculationIsNotKeptWhenItsAlertCannotBeQueued()
    {
        GiveUnallocatedMoney();
        services.EmailRequests.IsDown = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => services.CreateCalculation().CalculateAsync(services.UserId, October));

        Assert.Null(services.User.LastSuggestionMonth);
        Assert.Empty(services.SavedSuggestionsOfUser());
        Assert.Empty(services.Alerts.SavedAlerts);
    }

    // La tâche de fond calcule le mois sans que l'utilisateur ouvre l'application, et n'a plus rien à faire ensuite.
    [Fact]
    public async Task BackgroundCalculationSendsTheAlertWithoutAnyRead()
    {
        GiveUnallocatedMoney();
        var calculation = services.CreateCalculation();

        var dueUsers = await calculation.FindUsersToCalculateAsync(October, null, 100);
        var isCalculated = await calculation.CalculateAsync(services.UserId, October);
        var dueUsersAfter = await calculation.FindUsersToCalculateAsync(October, null, 100);

        Assert.Equal(services.UserId, Assert.Single(dueUsers));
        Assert.True(isCalculated);
        Assert.Empty(dueUsersAfter);
        Assert.Single(QueuedAlertEmails());
    }

    // Seuls les comptes confirmés qui ont au moins une opération et un mois à calculer attendent la tâche de fond,
    // dans l'ordre de leur identifiant, à partir de celui qui suit le dernier lu.
    [Fact]
    public async Task BackgroundTaskFindsOnlyConfirmedAccountsWithTransactionsInIdOrder()
    {
        GiveUnallocatedMoney();
        var unconfirmed = AddUser(isEmailConfirmed: false, withTransaction: true);
        AddUser(isEmailConfirmed: true, withTransaction: false);
        var alreadyCalculated = AddUser(isEmailConfirmed: true, withTransaction: true);
        alreadyCalculated.NoteSuggestionMonth(October);
        var other = AddUser(isEmailConfirmed: true, withTransaction: true);
        var calculation = services.CreateCalculation();

        var dueUsers = await calculation.FindUsersToCalculateAsync(October, null, 100);
        var afterFirst = await calculation.FindUsersToCalculateAsync(October, dueUsers[0], 100);
        var firstOnly = await calculation.FindUsersToCalculateAsync(October, null, 1);

        var expected = new List<Guid> { services.UserId, other.Id }.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToList();
        Assert.Equal(expected, dueUsers);
        Assert.Equal(expected.Skip(1).ToList(), afterFirst);
        Assert.Equal(expected.Take(1).ToList(), firstOnly);
        Assert.DoesNotContain(unconfirmed.Id, dueUsers);
    }

    // Un sac qui atteint son objectif grâce à une suggestion acceptée envoie son alerte avec le mouvement.
    [Fact]
    public async Task AcceptedSuggestionThatReachesAGoalSendsTheBagAlert()
    {
        services.AddBankAccount(100_000);
        var bag = services.SaveBag("Épargne");
        bag.ReplaceGoal(30_000, null, UsersCalendar.FindToday(services.Bank.Clock.GetUtcNow()));
        var suggestion = services.SaveSuggestion(SuggestionRule.UnallocatedMoney, October, 30_000);

        await services.CreateAcceptance().AcceptAsync(services.UserId, suggestion.Id, bag.Id, 30_000);

        var alert = Assert.Single(services.Alerts.SavedAlerts);
        Assert.Equal(AlertKind.BagGoalReached, alert.Kind);
        Assert.Equal(bag.Id, alert.BagId);
        Assert.Single(QueuedAlertEmails());
    }

    // Garde un autre compte, avec une opération d'octobre si le test le demande.
    private User AddUser(bool isEmailConfirmed, bool withTransaction)
    {
        var user = new User { Email = Guid.NewGuid().ToString("N") + "@exemple.test", PasswordHash = "empreinte-de-test", IsEmailConfirmed = isEmailConfirmed };
        services.Bags.SavedUsers.Add(user);
        services.Bank.Categories.SavedCategories.AddRange(DefaultCategories.CreateForUser(user.Id));

        if (withTransaction)
        {
            var account = services.AddBankAccount(1_000, user.Id);
            services.Bank.Transactions.SavedTransactions.Add(new BankTransaction
            {
                ExternalTransactionId = "autre-" + Guid.NewGuid(),
                TransactionDate = October.AddDays(4),
                Label = "OPERATION DE TEST",
                AmountInCents = -100,
                BankAccountId = account.Id,
                CategoryId = services.Bank.Categories.SavedCategories.First(category => category.UserId == user.Id).Id
            });
        }

        return user;
    }
}
