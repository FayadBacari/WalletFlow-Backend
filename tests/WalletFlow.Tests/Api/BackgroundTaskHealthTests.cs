using WalletFlow.Api.BackgroundTasks;
using static WalletFlow.Api.BackgroundTasks.BackgroundTaskHealth;

namespace WalletFlow.Tests.Api;

// Vérifie le bilan de santé des tâches de fond : une tâche est en panne après 3 passages de suite en échec, sans compter les
// passages à vide, ou quand elle ne termine plus aucun passage depuis deux attentes et 5 minutes.
public class BackgroundTaskHealthTests
{
    private const string TaskName = "TacheDeTest";
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 11, 19, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    // Crée un bilan qui suit la tâche de test depuis le début.
    private static BackgroundTaskHealth CreateHealth()
    {
        var health = new BackgroundTaskHealth();
        health.Watch(TaskName, Interval, Start);

        return health;
    }

    [Fact]
    public void ThirdFailedPassInARowIsAnAlert()
    {
        var health = CreateHealth();

        health.RecordPass(TaskName, PassOutcome.Failed, Start);
        health.RecordPass(TaskName, PassOutcome.Failed, Start);
        var afterTwoFailures = health.FindFailingTasks(Start);
        health.RecordPass(TaskName, PassOutcome.Failed, Start);

        Assert.Empty(afterTwoFailures);
        Assert.Equal("TacheDeTest : 3 passages de suite en échec.", Assert.Single(health.FindFailingTasks(Start)));
    }

    // Un passage réussi remet le compte à zéro : seuls des échecs à la suite font une panne.
    [Fact]
    public void SuccessfulPassResetsTheFailures()
    {
        var health = CreateHealth();

        health.RecordPass(TaskName, PassOutcome.Failed, Start);
        health.RecordPass(TaskName, PassOutcome.Failed, Start);
        health.RecordPass(TaskName, PassOutcome.Succeeded, Start);
        health.RecordPass(TaskName, PassOutcome.Failed, Start);
        health.RecordPass(TaskName, PassOutcome.Failed, Start);

        Assert.Empty(health.FindFailingTasks(Start));
    }

    // Un passage à vide ne remet pas le compte à zéro : une panne cachée entre des passages sans rien à faire reste signalée.
    [Fact]
    public void IdlePassesDoNotHideFailures()
    {
        var health = CreateHealth();

        health.RecordPass(TaskName, PassOutcome.Failed, Start);
        health.RecordPass(TaskName, PassOutcome.Idle, Start);
        health.RecordPass(TaskName, PassOutcome.Failed, Start);
        health.RecordPass(TaskName, PassOutcome.Idle, Start);
        health.RecordPass(TaskName, PassOutcome.Failed, Start);

        Assert.Single(health.FindFailingTasks(Start));
    }

    // Une tâche qui ne termine plus aucun passage, bloquée, est en panne passé deux attentes et 5 minutes, et pas avant.
    [Fact]
    public void TaskThatFinishesNoPassForTooLongIsAnAlert()
    {
        var health = CreateHealth();
        var deadline = Start + Interval * 2 + BackgroundTaskHealth.LateMargin;

        Assert.Empty(health.FindFailingTasks(deadline));
        Assert.Equal(
            "TacheDeTest : aucun passage terminé depuis 2026-11-19 08:00:00Z.",
            Assert.Single(health.FindFailingTasks(deadline + TimeSpan.FromSeconds(1))));
    }

    // Le retard se compte depuis le dernier passage terminé, même à vide : une file vide n'est pas une tâche bloquée.
    [Theory]
    [InlineData(PassOutcome.Succeeded)]
    [InlineData(PassOutcome.Idle)]
    [InlineData(PassOutcome.Failed)]
    public void LatenessCountsFromTheLastFinishedPass(PassOutcome outcome)
    {
        var health = CreateHealth();
        var lastPass = Start + TimeSpan.FromHours(10);

        health.RecordPass(TaskName, outcome, lastPass);

        Assert.Empty(health.FindFailingTasks(lastPass + Interval * 2 + BackgroundTaskHealth.LateMargin));
        Assert.Single(health.FindFailingTasks(lastPass + Interval * 2 + BackgroundTaskHealth.LateMargin + TimeSpan.FromSeconds(1)));
    }

    // Chaque tâche a son propre bilan : la panne de l'une ne touche pas l'autre.
    [Fact]
    public void EachTaskHasItsOwnRecord()
    {
        var health = CreateHealth();
        health.Watch("AutreTache", Interval, Start);

        for (var failure = 1; failure <= FailedPassesBeforeAlert; failure++)
        {
            health.RecordPass("AutreTache", PassOutcome.Failed, Start);
        }

        Assert.StartsWith("AutreTache :", Assert.Single(health.FindFailingTasks(Start)));
    }
}
