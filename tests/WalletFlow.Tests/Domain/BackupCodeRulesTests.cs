using WalletFlow.Domain.UsersAndSecurity;

namespace WalletFlow.Tests.Domain;

// Vérifie qu'un code de secours ne sert qu'une seule fois (GR Auth).
public class BackupCodeRulesTests
{
    private static readonly DateTime FirstUseDate = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NewBackupCodeHasNeverBeenUsed()
    {
        Assert.Null(new BackupCode().UsedAt);
    }

    [Fact]
    public void BackupCodeCanBeUsedOnlyOnce()
    {
        var backupCode = new BackupCode();

        backupCode.MarkAsUsed(FirstUseDate);

        Assert.Throws<InvalidOperationException>(() => backupCode.MarkAsUsed(FirstUseDate.AddMinutes(1)));
        Assert.Equal(FirstUseDate, backupCode.UsedAt);
    }
}
