using WalletFlow.Application.DataExport;

namespace WalletFlow.Infrastructure.Security;

// Compte en mémoire les essais de téléchargement des données de chaque utilisateur : 3 par heure (SS §6). L'heure part du
// premier essai. Les compteurs repartent à zéro si l'API redémarre, comme les autres limites.
public class InMemoryDataExportLimiter : IDataExportLimiter
{
    public const int MaximumExportsPerWindow = 3;
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly TimeProvider clock;
    private readonly Dictionary<Guid, ExportWindow> windowByUser = new Dictionary<Guid, ExportWindow>();
    private readonly object windowsLock = new object();

    // Reçoit l'horloge qui mesure l'heure.
    public InMemoryDataExportLimiter(TimeProvider clock)
    {
        this.clock = clock;
    }

    // Compte un essai si cet utilisateur en a fait moins de 3 depuis le début de son heure.
    public bool TryCountExport(Guid userId)
    {
        var now = clock.GetUtcNow();

        lock (windowsLock)
        {
            RemoveFinishedWindows(now);

            if (!windowByUser.TryGetValue(userId, out var window))
            {
                windowByUser[userId] = new ExportWindow(now, 1);
                return true;
            }

            if (window.ExportCount >= MaximumExportsPerWindow)
            {
                return false;
            }

            windowByUser[userId] = window with { ExportCount = window.ExportCount + 1 };
            return true;
        }
    }

    // Oublie les utilisateurs dont l'heure est finie, pour que la mémoire ne grossisse pas.
    private void RemoveFinishedWindows(DateTimeOffset now)
    {
        var finishedUsers = windowByUser
            .Where(entry => now - entry.Value.StartedAt >= Window)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var userId in finishedUsers)
        {
            windowByUser.Remove(userId);
        }
    }

    // L'heure d'un utilisateur : son début, et les essais comptés depuis.
    private sealed record ExportWindow(DateTimeOffset StartedAt, int ExportCount);
}
