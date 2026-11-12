using WalletFlow.Tests.Fakes;

namespace WalletFlow.Tests.Application;

// Vérifie le solde total, tous comptes confondus (GR Comptes, SS §4, DD §11).
public class BankBalanceTotalServiceTests
{
    private readonly BankServices services = new BankServices();

    // GR Comptes : chaque compte compte avec son dernier solde connu, même quand sa connexion a expiré,
    // et la date du plus ancien solde dit de quand date le total.
    [Fact]
    public async Task EveryLastKnownBalanceCounts()
    {
        var firstConnection = await services.ConnectAsync(
            new TestBankAccount("stable-1", "Compte courant", "EUR", 10_000),
            new TestBankAccount("stable-2", "Compte joint", "EUR", -2_500));
        var oldestUpdate = services.Now;
        services.Clock.Advance(TimeSpan.FromDays(2));
        await services.ConnectAsync(new TestBankAccount("stable-3", "Livret A", "EUR", 500_000));
        firstConnection.MarkExpired();

        var total = await services.CreateBalanceTotal().ReadAsync(services.UserId);

        Assert.Equal(507_500, total.TotalInCents);
        Assert.Equal(oldestUpdate, total.OldestBalanceUpdatedAt);
        Assert.Equal(3, total.AccountCount);
    }

    // Sans compte, le total vaut zéro, et il n'a pas de date.
    [Fact]
    public async Task NoAccountGivesZero()
    {
        var total = await services.CreateBalanceTotal().ReadAsync(services.UserId);

        Assert.Equal(0, total.TotalInCents);
        Assert.Null(total.OldestBalanceUpdatedAt);
        Assert.Equal(0, total.AccountCount);
    }

    // SS §4 : les comptes d'un autre utilisateur ne comptent jamais.
    [Fact]
    public async Task AccountsOfAnotherUserNeverCount()
    {
        await services.ConnectAsync(new TestBankAccount("stable-1", "Compte courant", "EUR", 10_000));

        var total = await services.CreateBalanceTotal().ReadAsync(Guid.NewGuid());

        Assert.Equal(0, total.TotalInCents);
    }

    // Trois comptes au solde maximum dépassent ce qu'un int peut tenir : le total ne déborde pas.
    [Fact]
    public async Task TotalDoesNotOverflow()
    {
        await services.ConnectAsync(
            new TestBankAccount("stable-1", "Compte 1", "EUR", 1_000_000_000),
            new TestBankAccount("stable-2", "Compte 2", "EUR", 1_000_000_000),
            new TestBankAccount("stable-3", "Compte 3", "EUR", 1_000_000_000));

        var total = await services.CreateBalanceTotal().ReadAsync(services.UserId);

        Assert.Equal(3_000_000_000L, total.TotalInCents);
    }
}
