using Microsoft.EntityFrameworkCore;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Infrastructure.Persistence;
using static WalletFlow.Tests.Infrastructure.BankTestRows;

namespace WalletFlow.Tests.Infrastructure;

// Les lignes d'un compte, une de chaque sorte, pour vérifier ce que son fichier contient, ce qu'un autre compte n'y voit pas,
// et ce que son effacement emporte (GR Compte, GR Suppression).
public sealed record FullAccountRows(
    User Account,
    BankConnection Connection,
    BankAccount BankAccount,
    List<BankTransaction> Transactions,
    List<Category> Categories,
    Category Leisure,
    List<Bag> Bags,
    List<BagMovement> Movements,
    SavingsSuggestion Suggestion,
    Alert Alert,
    SupportRequest Request,
    SupportResponse Response)
{
    // Enregistre un compte avec une ligne de chaque sorte : son nom, son canal, une banque et deux opérations, ses catégories et
    // un budget, une règle, deux sacs et deux déplacements, une suggestion acceptée, une alerte coupée et une envoyée, une demande
    // de support et sa réponse.
    public static async Task<FullAccountRows> SaveAsync(WalletFlowDatabaseContext databaseContext, string email, string stableId)
    {
        var savedAccount = await SaveAccountAsync(databaseContext, email);
        var (connection, bankAccount) = await SaveConnectionAsync(databaseContext, savedAccount.Id, stableId);
        var account = await databaseContext.Users.SingleAsync(user => user.Id == savedAccount.Id);
        account.ReplaceName("Durand", "Alice");
        account.ChangeNotificationChannel(NotificationChannel.EmailAndPhone);
        var categories = await databaseContext.Categories.Where(category => category.UserId == account.Id).ToListAsync();
        var leisure = categories.First(category => !category.IsOtherCategory);
        leisure.ReplaceMonthlyBudget(20_000);
        var transfer = CreateTransaction(bankAccount.Id, leisure.Id, stableId + "-virement", "VIR VERS LIVRET", -5000, new DateOnly(2026, 11, 3));
        transfer.MarkAsInternalTransfer(true);
        var transactions = new List<BankTransaction> { transfer, CreateTransaction(bankAccount.Id, leisure.Id, stableId + "-carte") };
        var holidays = new Bag { Name = "Vacances", TargetAmountInCents = 100_000, TargetDate = new DateOnly(2027, 7, 1), UserId = account.Id };
        var car = new Bag { Name = "Voiture", UserId = account.Id };
        var suggestion = new SavingsSuggestion
        {
            Rule = SuggestionRule.UnallocatedMoney,
            Month = new DateOnly(2026, 10, 1),
            MonthlySavingsInCents = 2500,
            Status = SuggestionStatus.Accepted,
            StatusChangedAt = Now.AddDays(-2),
            UserId = account.Id
        };
        var movements = new List<BagMovement>
        {
            new BagMovement { AmountInCents = 3000, MovedAt = Now.AddDays(-2), DestinationBagId = holidays.Id, SavingsSuggestionId = suggestion.Id },
            new BagMovement { AmountInCents = 1000, MovedAt = Now.AddDays(-1), SourceBagId = holidays.Id, DestinationBagId = car.Id }
        };
        var budgetType = await databaseContext.AlertTypes.AsNoTracking().SingleAsync(type => type.Kind == AlertKind.BudgetReached);
        var alert = Alert.ForBagGoal(account.Id, holidays.Id, Now);
        var request = SupportRequest.FromUser(account.Id, "Où est mon sac ?", Now.AddDays(-3));
        var response = SupportResponse.Create(request.Id, "Dans la liste des sacs.", Now.AddDays(-2));

        databaseContext.BankTransactions.AddRange(transactions);
        databaseContext.CategorizationRules.Add(new CategorizationRule { Label = "CB CARREFOUR", CategoryId = leisure.Id });
        databaseContext.Bags.AddRange(holidays, car);
        databaseContext.SavingsSuggestions.Add(suggestion);
        databaseContext.BagMovements.AddRange(movements);
        databaseContext.AlertPreferences.Add(AlertPreference.Create(account.Id, budgetType, false));
        databaseContext.Alerts.Add(alert);
        databaseContext.SupportRequests.Add(request);
        databaseContext.SupportResponses.Add(response);
        await databaseContext.SaveChangesAsync();
        databaseContext.ChangeTracker.Clear();

        return new FullAccountRows(account, connection, bankAccount, transactions, categories, leisure, new List<Bag> { holidays, car }, movements, suggestion, alert, request, response);
    }
}
