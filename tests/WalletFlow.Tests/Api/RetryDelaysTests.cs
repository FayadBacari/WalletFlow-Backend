using WalletFlow.Api.Configuration;
using WalletFlow.Domain.BagsAndSuggestions;
using WalletFlow.Domain.NotificationsAndSupport;
using WalletFlow.Infrastructure.BankData;
using WalletFlow.Infrastructure.Security;

namespace WalletFlow.Tests.Api;

// Vérifie les délais de l'en-tête Retry-After : chacun reste la fenêtre de sa limite, et s'écrit en secondes arrondies au-dessus.
public class RetryDelaysTests
{
    // Chaque délai d'une limite propre à une action reste égal à la fenêtre que compte cette limite.
    [Fact]
    public void EachDelayIsTheWindowOfItsLimit()
    {
        Assert.Equal(InMemoryEmailAddressRequestLimiter.CountingWindow, RetryDelays.PerEmailAddress);
        Assert.Equal(InMemoryDataExportLimiter.Window, RetryDelays.DataExport);
        Assert.Equal(InMemoryBankUpdateLimiter.UpdateInterval, RetryDelays.BankUpdate);
        Assert.Equal(SupportRules.RequestDay, RetryDelays.SupportRequests);
        Assert.Equal(BagRules.MovementDay, RetryDelays.BagMovements);
    }

    // Un délai s'écrit en secondes entières, arrondies au-dessus, et jamais sous 1 seconde.
    [Theory]
    [InlineData(0, "1")]
    [InlineData(200, "1")]
    [InlineData(59_100, "60")]
    [InlineData(60_000, "60")]
    [InlineData(3_600_000, "3600")]
    public void DelayIsWrittenInWholeSecondsRoundedUp(int milliseconds, string expected)
    {
        Assert.Equal(expected, RetryDelays.InSeconds(TimeSpan.FromMilliseconds(milliseconds)));
    }
}
