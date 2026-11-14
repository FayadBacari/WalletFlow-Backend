using WalletFlow.Application.Bags;
using WalletFlow.Application.BankConnections;
using WalletFlow.Application.Persistence;
using WalletFlow.Domain.AccountsAndCategories;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.UsersAndSecurity;
using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Crée les cas d'utilisation des sacs avec une fausse base et une fausse horloge, pour un utilisateur sans sac.
// Son solde total est la somme des comptes bancaires que le test lui donne.
public class BagServices
{
    public User User { get; } = new User { Email = "sacs@exemple.test", PasswordHash = "empreinte-de-test", IsEmailConfirmed = true };
    public FakeTimeProvider Clock { get; } = new FakeTimeProvider();
    public FakeBagRepository Repository { get; } = new FakeBagRepository();
    public FakeBankConnectionRepository Bank { get; } = new FakeBankConnectionRepository();
    public FakeDatabaseUnitOfWork UnitOfWork { get; }

    // Garde le compte de l'utilisateur dans la fausse base, et relie la fausse base à la fausse unité de travail.
    public BagServices()
    {
        Repository.SavedUsers.Add(User);
        UnitOfWork = new FakeDatabaseUnitOfWork(Repository);
    }

    // L'identifiant de l'utilisateur des tests.
    public Guid UserId => User.Id;

    // L'heure de l'horloge des tests.
    public DateTime Now => Clock.GetUtcNow().UtcDateTime;

    // Le jour de l'horloge des tests, à Paris : 12 h en temps universel est déjà le même jour à Paris.
    public DateOnly Today => UsersCalendar.FindToday(Clock.GetUtcNow());

    // Crée le service qui liste les sacs.
    public BagListService CreateList()
    {
        return new BagListService(Repository, new BankBalanceTotalService(Bank));
    }

    // Crée le service qui crée un sac.
    public BagCreationService CreateCreation()
    {
        return new BagCreationService(Repository, new DatabaseChangesSavingService(UnitOfWork), Clock);
    }

    // Crée le service qui renomme un sac.
    public BagRenameService CreateRename()
    {
        return new BagRenameService(Repository, new DatabaseChangesSavingService(UnitOfWork));
    }

    // Crée le service qui fixe l'objectif d'un sac.
    public BagGoalService CreateGoal()
    {
        return new BagGoalService(Repository, new DatabaseChangesSavingService(UnitOfWork), Clock);
    }

    // Crée le service qui supprime un sac.
    public BagDeletionService CreateDeletion()
    {
        return new BagDeletionService(Repository, new DatabaseChangesSavingService(UnitOfWork));
    }

    // Crée le service qui déplace de l'argent.
    public BagMovementService CreateMovement()
    {
        return new BagMovementService(Repository, new BankBalanceTotalService(Bank), new DatabaseChangesSavingService(UnitOfWork), Clock);
    }

    // Crée le service qui lit l'historique d'un sac.
    public BagHistoryService CreateHistory()
    {
        return new BagHistoryService(Repository);
    }

    // Donne à l'utilisateur un compte bancaire avec ce solde, dans sa propre connexion.
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
        Bank.SavedConnections.Add(connection);
        Bank.SavedAccounts.Add(account);

        return account;
    }

    // Garde directement un sac de l'utilisateur, ou d'un autre utilisateur.
    public Bag SaveBag(string name, Guid? userId = null)
    {
        var bag = new Bag { Name = name, UserId = userId ?? UserId };
        Repository.SavedBags.Add(bag);

        return bag;
    }

    // Garde directement un mouvement, daté de maintenant si le test ne donne pas de date.
    public BagMovement SaveMovement(Bag? source, Bag? destination, int amountInCents, DateTime? movedAt = null)
    {
        var movement = new BagMovement
        {
            AmountInCents = amountInCents,
            MovedAt = movedAt ?? Now,
            SourceBagId = source?.Id,
            DestinationBagId = destination?.Id
        };
        Repository.SavedMovements.Add(movement);

        return movement;
    }

    // Calcule le solde gardé d'un sac : les mouvements arrivés moins les mouvements partis.
    public long BalanceOf(Bag bag)
    {
        long balanceInCents = 0;

        foreach (var movement in Repository.SavedMovements)
        {
            if (movement.DestinationBagId == bag.Id)
            {
                balanceInCents = balanceInCents + movement.AmountInCents;
            }

            if (movement.SourceBagId == bag.Id)
            {
                balanceInCents = balanceInCents - movement.AmountInCents;
            }
        }

        return balanceInCents;
    }
}
