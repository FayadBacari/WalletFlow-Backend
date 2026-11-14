using WalletFlow.Application.Bags;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Persistence;
using WalletFlow.Application.Suggestions;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Crée les cas d'utilisation des suggestions avec une fausse base et une fausse horloge, pour un utilisateur qui a ses
// 8 catégories par défaut, sans compte bancaire, sans opération et sans sac. L'horloge des tests marque le 4 novembre 2026
// à midi : le mois évalué est octobre 2026, et sa moyenne porte sur juillet, août et septembre.
public class SuggestionServices
{
    public static readonly DateOnly October = new DateOnly(2026, 10, 1);
    public static readonly DateOnly September = new DateOnly(2026, 9, 1);
    public static readonly DateOnly July = new DateOnly(2026, 7, 1);

    public BankServices Bank { get; } = new BankServices();
    public FakeBagRepository Bags { get; } = new FakeBagRepository();
    public FakeSuggestionRepository Suggestions { get; }
    public FakeDatabaseUnitOfWork UnitOfWork { get; }
    public User User { get; }

    // Garde le compte de l'utilisateur dans la fausse base, et relie les fausses bases à la fausse unité de travail.
    public SuggestionServices()
    {
        User = new User { Id = Bank.UserId, Email = "suggestions@exemple.test", PasswordHash = "empreinte-de-test", IsEmailConfirmed = true };
        Bags.SavedUsers.Add(User);
        Suggestions = new FakeSuggestionRepository(Bags);
        UnitOfWork = new FakeDatabaseUnitOfWork(Bags, Suggestions);
    }

    // L'identifiant de l'utilisateur des tests.
    public Guid UserId => User.Id;

    // L'heure de l'horloge des tests.
    public DateTime Now => Bank.Now;

    // Crée le service qui calcule et liste les suggestions.
    public SuggestionListService CreateList()
    {
        return new SuggestionListService(
            Bags,
            Suggestions,
            Bank.Transactions,
            Bank.Categories,
            new BankBalanceTotalService(Bank.Repository),
            new DatabaseChangesSavingService(UnitOfWork),
            Bank.Clock);
    }

    // Crée le service qui accepte une suggestion.
    public SuggestionAcceptanceService CreateAcceptance()
    {
        return new SuggestionAcceptanceService(
            Bags,
            Suggestions,
            new BagMovementChecks(Bags, new BankBalanceTotalService(Bank.Repository)),
            new DatabaseChangesSavingService(UnitOfWork),
            Bank.Clock);
    }

    // Crée le service qui ignore une suggestion.
    public SuggestionDismissalService CreateDismissal()
    {
        return new SuggestionDismissalService(Bags, Suggestions, new DatabaseChangesSavingService(UnitOfWork), Bank.Clock);
    }

    // Donne à l'utilisateur, ou à un autre, un compte bancaire avec ce solde, dans sa propre connexion.
    public BankAccount AddBankAccount(int balanceInCents, Guid? userId = null)
    {
        var connection = new BankConnection
        {
            ExternalConnectionId = "session-" + Guid.NewGuid(),
            BankName = "Banque de test",
            AuthorizationExpiresAt = Now.AddDays(100),
            UserId = userId ?? UserId
        };
        var account = new BankAccount
        {
            ExternalAccountId = "compte-" + Guid.NewGuid(),
            Name = "Compte courant",
            BalanceInCents = balanceInCents,
            BalanceUpdatedAt = Now,
            BankConnectionId = connection.Id
        };
        Bank.Repository.SavedConnections.Add(connection);
        Bank.Repository.SavedAccounts.Add(account);

        return account;
    }

    // Garde une opération de ce compte dans la catégorie de l'utilisateur qui porte ce nom.
    public BankTransaction SaveTransaction(BankAccount account, string categoryName, DateOnly date, int amountInCents)
    {
        return Bank.SaveTransaction(account, categoryName, date, amountInCents);
    }

    // Fait commencer l'historique le 30 juin, juste avant les 3 mois de la moyenne : un centime reçu dans « Autres »,
    // qui n'entre dans aucun des mois calculés. Juillet, août, septembre et octobre sont alors connus en entier.
    public void StartHistoryBeforeJuly(BankAccount account)
    {
        SaveTransaction(account, "Autres", July.AddDays(-1), 1);
    }

    // Garde une dépense de ce montant, en centimes, le 10 de chaque mois de juillet à octobre, dans la catégorie.
    // Le premier montant est celui de juillet, le dernier celui d'octobre.
    public void SaveMonthlyExpenses(BankAccount account, string categoryName, params int[] amountsInCents)
    {
        for (var index = 0; index < amountsInCents.Length; index++)
        {
            SaveTransaction(account, categoryName, July.AddMonths(index).AddDays(9), -amountsInCents[index]);
        }
    }

    // Donne la catégorie de l'utilisateur des tests qui porte ce nom.
    public Category CategoryNamed(string name)
    {
        return Bank.CategoryNamed(name);
    }

    // Garde directement un sac de l'utilisateur, ou d'un autre utilisateur.
    public Bag SaveBag(string name, Guid? userId = null)
    {
        var bag = new Bag { Name = name, UserId = userId ?? UserId };
        Bags.SavedBags.Add(bag);

        return bag;
    }

    // Garde directement une suggestion de l'utilisateur, ou d'un autre utilisateur.
    public SavingsSuggestion SaveSuggestion(
        SuggestionRule rule,
        DateOnly month,
        int savingsInCents,
        SuggestionStatus status = SuggestionStatus.Proposed,
        Guid? categoryId = null,
        DateTime? statusChangedAt = null,
        Guid? userId = null)
    {
        var suggestion = new SavingsSuggestion
        {
            Rule = rule,
            Month = month,
            MonthlySavingsInCents = savingsInCents,
            Status = status,
            StatusChangedAt = statusChangedAt,
            UserId = userId ?? UserId,
            CategoryId = categoryId
        };
        Suggestions.SavedSuggestions.Add(suggestion);

        return suggestion;
    }

    // Donne les suggestions gardées de l'utilisateur, pour vérifier ce que la base contient.
    public List<SavingsSuggestion> SavedSuggestionsOfUser()
    {
        return Suggestions.SavedSuggestions.Where(suggestion => suggestion.UserId == UserId).ToList();
    }
}
