using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie la règle commune à tous les liens envoyés par e-mail (SS §2).
public class EmailTokenRulesTests
{
    private static readonly DateTime ExpiresAt = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void LinkCanBeUsedJustBeforeItsExpirationDate()
    {
        var token = new EmailToken { ExpiresAt = ExpiresAt };

        Assert.True(token.CanBeUsedAt(ExpiresAt.AddTicks(-1)));
    }

    [Fact]
    public void LinkExpiresAtItsExactExpirationDate()
    {
        var token = new EmailToken { ExpiresAt = ExpiresAt };

        Assert.False(token.CanBeUsedAt(ExpiresAt));
    }

    [Fact]
    public void LinkAlreadyUsedCanNeverBeUsedAgain()
    {
        var token = new EmailToken { ExpiresAt = ExpiresAt, UsedAt = ExpiresAt.AddHours(-1) };

        Assert.False(token.CanBeUsedAt(ExpiresAt.AddHours(-1)));
    }

    // Les durées exactes de la SS §2.
    [Fact]
    public void LinksLastTheDurationsWrittenInTheSecurityStrategy()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), EmailTokenValidityDurations.PasswordRecoveryLink);
        Assert.Equal(TimeSpan.FromHours(1), EmailTokenValidityDurations.AccountUnlockLink);
        Assert.Equal(TimeSpan.FromHours(24), EmailTokenValidityDurations.SignupConfirmationLink);
    }

    // Un lien ne sert qu'une seule fois (SS §2) : le marquer une seconde fois est une erreur du code, jamais un cas normal.
    [Fact]
    public void LinkMarkedAsUsedCannotBeMarkedAgain()
    {
        var token = new EmailToken { ExpiresAt = ExpiresAt };
        var firstUseDate = ExpiresAt.AddMinutes(-10);

        token.MarkAsUsed(firstUseDate);

        Assert.Throws<InvalidOperationException>(() => token.MarkAsUsed(ExpiresAt.AddMinutes(-5)));
        Assert.Equal(firstUseDate, token.UsedAt);
        Assert.False(token.CanBeUsedAt(ExpiresAt.AddMinutes(-1)));
    }
}
