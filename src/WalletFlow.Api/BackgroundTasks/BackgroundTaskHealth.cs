using System.Globalization;

namespace WalletFlow.Api.BackgroundTasks;

// Le bilan des passages de chaque tâche de fond, que lit le point de santé des tâches (voir HealthConfiguration). Une tâche est
// en panne après plusieurs passages de suite en échec, ou quand elle ne termine plus aucun passage depuis trop longtemps : elle est
// alors bloquée. Un passage sans rien à faire, comme une file d'e-mails vide, ne remet pas les échecs à zéro : sinon, une panne du
// serveur d'e-mails, cachée entre des passages à vide, ne serait jamais signalée. Le bilan est gardé en mémoire : l'API ne tourne
// qu'en une seule instance (voir SingleInstanceGuard).
public sealed class BackgroundTaskHealth
{
    // Au-delà de ce nombre de passages de suite en échec, la tâche est en panne : un échec isolé passe au passage suivant.
    public const int FailedPassesBeforeAlert = 3;

    // Le temps laissé en plus de deux attentes à une tâche pour terminer un passage, avant de la dire bloquée.
    public static readonly TimeSpan LateMargin = TimeSpan.FromMinutes(5);

    private readonly object gate = new object();
    private readonly Dictionary<string, TaskRecord> records = new Dictionary<string, TaskRecord>();

    // Le résultat d'un passage.
    public enum PassOutcome
    {
        // Toutes ses étapes ont réussi.
        Succeeded,

        // Au moins une étape a échoué.
        Failed,

        // Il n'avait rien à faire : il ne dit rien de la santé de la tâche.
        Idle
    }

    // Ce que le bilan retient d'une tâche.
    private sealed class TaskRecord(TimeSpan interval, DateTimeOffset watchedSince)
    {
        public TimeSpan Interval { get; } = interval;

        public DateTimeOffset LastPassOrStart { get; set; } = watchedSince;

        public int ConsecutiveFailures { get; set; } = 0;
    }

    // Commence à suivre une tâche, quand sa boucle démarre.
    public void Watch(string taskName, TimeSpan interval, DateTimeOffset now)
    {
        lock (gate)
        {
            records[taskName] = new TaskRecord(interval, now);
        }
    }

    // Note un passage terminé et son résultat.
    public void RecordPass(string taskName, PassOutcome outcome, DateTimeOffset now)
    {
        lock (gate)
        {
            var record = records[taskName];
            record.LastPassOrStart = now;

            if (outcome == PassOutcome.Succeeded)
            {
                record.ConsecutiveFailures = 0;
            }
            else if (outcome == PassOutcome.Failed)
            {
                record.ConsecutiveFailures = record.ConsecutiveFailures + 1;
            }
        }
    }

    // Donne les tâches en panne à cet instant, chacune avec sa raison. La liste est vide si toutes vont bien.
    public List<string> FindFailingTasks(DateTimeOffset now)
    {
        var failingTasks = new List<string>();

        lock (gate)
        {
            foreach (var (taskName, record) in records.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                if (record.ConsecutiveFailures >= FailedPassesBeforeAlert)
                {
                    failingTasks.Add(taskName + " : " + record.ConsecutiveFailures + " passages de suite en échec.");
                }
                else if (now - record.LastPassOrStart > record.Interval * 2 + LateMargin)
                {
                    failingTasks.Add(taskName + " : aucun passage terminé depuis "
                        + record.LastPassOrStart.ToString("u", CultureInfo.InvariantCulture) + ".");
                }
            }
        }

        return failingTasks;
    }
}
