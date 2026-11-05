using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie la règle commune à tous les liens envoyés par e-mail (SS §2).
public class EmailTokenRulesTests
{
    private static readonly DateTime ExpirationDateOfTheLinkInTheseTests = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void LinkCanBeUsedJustBeforeItsExpirationDate()
    {
        var tokenOfTheLinkBeingTestedNow = new EmailToken { ExpiresAt = ExpirationDateOfTheLinkInTheseTests };

        Assert.True(EmailTokenRules.CheckThatTokenCanStillBeUsedAtThisDate(tokenOfTheLinkBeingTestedNow, ExpirationDateOfTheLinkInTheseTests.AddTicks(-1)));
    }

    [Fact]
    public void LinkExpiresAtItsExactExpirationDate()
    {
        var tokenOfTheLinkBeingTestedNow = new EmailToken { ExpiresAt = ExpirationDateOfTheLinkInTheseTests };

        Assert.False(EmailTokenRules.CheckThatTokenCanStillBeUsedAtThisDate(tokenOfTheLinkBeingTestedNow, ExpirationDateOfTheLinkInTheseTests));
    }

    [Fact]
    public void LinkAlreadyUsedCanNeverBeUsedAgain()
    {
        var tokenOfTheLinkBeingTestedNow = new EmailToken { ExpiresAt = ExpirationDateOfTheLinkInTheseTests, UsedAt = ExpirationDateOfTheLinkInTheseTests.AddHours(-1) };

        Assert.False(EmailTokenRules.CheckThatTokenCanStillBeUsedAtThisDate(tokenOfTheLinkBeingTestedNow, ExpirationDateOfTheLinkInTheseTests.AddHours(-1)));
    }

    // Les durées exactes de la SS §2.
    [Fact]
    public void LinksLastTheDurationsWrittenInTheSecurityStrategy()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), EmailTokenValidityDurations.ValidityDurationOfThePasswordRecoveryLink);
        Assert.Equal(TimeSpan.FromHours(1), EmailTokenValidityDurations.ValidityDurationOfTheAccountUnlockLink);
        Assert.Equal(TimeSpan.FromHours(24), EmailTokenValidityDurations.ValidityDurationOfTheSignupConfirmationLink);
    }
}
