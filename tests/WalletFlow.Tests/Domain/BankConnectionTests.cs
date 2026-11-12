using WalletFlow.Domain.AccountsAndCategories;

namespace WalletFlow.Tests.Domain;

// Vérifie que la connexion bancaire et le compte protègent leurs propres règles (GR Comptes, DD §5).
public class BankConnectionTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);

    // Crée une connexion avec cet état, dont l'autorisation finit dans cette durée.
    private static BankConnection CreateConnection(BankConnectionStatus status, TimeSpan remainingTime)
    {
        return new BankConnection
        {
            ExternalConnectionId = "session-de-test",
            BankName = "Banque de test",
            Status = status,
            AuthorizationExpiresAt = Now + remainingTime,
            UserId = Guid.NewGuid()
        };
    }

    // GR Comptes : l'utilisateur est prévenu 7 jours avant la fin, pas avant, et plus une fois la fin passée.
    [Fact]
    public void UserIsWarnedSevenDaysBeforeTheEnd()
    {
        Assert.True(CreateConnection(BankConnectionStatus.Active, TimeSpan.FromDays(7)).IsEndingSoon(Now));
        Assert.True(CreateConnection(BankConnectionStatus.InError, TimeSpan.FromMinutes(1)).IsEndingSoon(Now));
        Assert.False(CreateConnection(BankConnectionStatus.Active, TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1)).IsEndingSoon(Now));
        Assert.False(CreateConnection(BankConnectionStatus.Active, TimeSpan.Zero).IsEndingSoon(Now));
        Assert.False(CreateConnection(BankConnectionStatus.Expired, TimeSpan.FromDays(3)).IsEndingSoon(Now));
    }

    [Fact]
    public void AuthorizationEndsAtItsDate()
    {
        Assert.True(CreateConnection(BankConnectionStatus.Active, TimeSpan.Zero).HasAuthorizationEnded(Now));
        Assert.False(CreateConnection(BankConnectionStatus.Active, TimeSpan.FromSeconds(1)).HasAuthorizationEnded(Now));
    }

    // GR Comptes : si la banque ne répond pas, la connexion passe en erreur, puis redevient active quand la banque répond.
    [Fact]
    public void ConnectionGoesInErrorThenComesBack()
    {
        var connection = CreateConnection(BankConnectionStatus.Active, TimeSpan.FromDays(30));

        connection.RecordBankFailure();
        Assert.Equal(BankConnectionStatus.InError, connection.Status);

        connection.RecordSuccessfulUpdate();
        Assert.Equal(BankConnectionStatus.Active, connection.Status);
    }

    // Une connexion expirée ne revient que par un renouvellement : ni une réponse, ni une panne ne la changent.
    [Fact]
    public void ExpiredConnectionComesBackOnlyThroughARenewal()
    {
        var connection = CreateConnection(BankConnectionStatus.Expired, TimeSpan.FromDays(30));

        connection.RecordSuccessfulUpdate();
        connection.RecordBankFailure();
        Assert.Equal(BankConnectionStatus.Expired, connection.Status);

        connection.Renew("nouvelle-session", Now.AddDays(180));
        Assert.Equal(BankConnectionStatus.Active, connection.Status);
        Assert.Equal("nouvelle-session", connection.ExternalConnectionId);
        Assert.Equal(Now.AddDays(180), connection.AuthorizationExpiresAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RenewalWithoutSessionIsRefused(string newSessionId)
    {
        var connection = CreateConnection(BankConnectionStatus.Expired, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => connection.Renew(newSessionId, Now.AddDays(180)));
        Assert.Equal("session-de-test", connection.ExternalConnectionId);
    }

    // La colonne id_connexion_externe fait 100 caractères (MPD).
    [Fact]
    public void RenewalWithATooLongSessionIsRefused()
    {
        var connection = CreateConnection(BankConnectionStatus.Expired, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => connection.Renew(new string('s', 101), Now.AddDays(180)));
    }

    [Fact]
    public void AccountBalanceIsReplacedWithItsDate()
    {
        var account = new BankAccount { ExternalAccountId = "compte", Name = "Compte courant", BalanceInCents = 100, BalanceUpdatedAt = Now };

        account.UpdateBalance(-2500, Now.AddHours(6));

        Assert.Equal(-2500, account.BalanceInCents);
        Assert.Equal(Now.AddHours(6), account.BalanceUpdatedAt);
    }

    // SS §5 : un solde hors des limites est une erreur du code appelant, et l'ancien solde reste.
    [Fact]
    public void BalanceOutsideTheLimitsIsRefused()
    {
        var account = new BankAccount { ExternalAccountId = "compte", Name = "Compte courant", BalanceInCents = 100, BalanceUpdatedAt = Now };

        Assert.Throws<ArgumentOutOfRangeException>(() => account.UpdateBalance(1_000_000_001, Now.AddHours(6)));
        Assert.Equal(100, account.BalanceInCents);
        Assert.Equal(Now, account.BalanceUpdatedAt);
    }

    // Le nom arrive déjà nettoyé : un nom avec des espaces autour est une erreur du code appelant.
    [Fact]
    public void AccountNameNotInItsSavedFormIsRefused()
    {
        var account = new BankAccount { ExternalAccountId = "compte", Name = "Compte courant", BalanceInCents = 100, BalanceUpdatedAt = Now };

        Assert.Throws<ArgumentException>(() => account.Rename(" Livret A "));
        account.Rename("Livret A");
        Assert.Equal("Livret A", account.Name);
    }
}
