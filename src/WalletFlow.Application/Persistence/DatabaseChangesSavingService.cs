namespace WalletFlow.Application.Persistence;

// Enregistre des changements même quand d'autres requêtes touchent les mêmes lignes au même instant.
// Cette règle est écrite ici une seule fois : chaque cas d'utilisation fournit seulement son passage « lire, vérifier, modifier ».
public class DatabaseChangesSavingService
{
    // Un conflit veut dire qu'une autre requête a réussi à écrire. Le compte se bloque au bout de 5 essais :
    // 10 passages suffisent donc largement. Au-delà, la demande est refusée, jamais acceptée sans contrôle.
    public const int MaximumTries = 10;

    private readonly IDatabaseUnitOfWork unitOfWork;

    // Reçoit l'unité de travail de la requête en cours.
    public DatabaseChangesSavingService(IDatabaseUnitOfWork unitOfWork)
    {
        this.unitOfWork = unitOfWork;
    }

    // Rejoue le passage jusqu'à ce que ses changements soient enregistrés. Au premier passage, un objet déjà lu par la requête
    // n'est pas relu : si une autre requête l'a changé entre-temps, c'est l'enregistrement qui le voit, et il échoue.
    // Après un conflit, tout est relu : les contrôles du passage se refont donc sur ce que l'autre requête vient d'écrire.
    // Si les autres requêtes ne laissent jamais passer l'enregistrement, donne la réponse de refus fournie.
    public async Task<TResult> SaveWithRetriesAsync<TResult>(
        Func<Task<PreparedChanges<TResult>>> prepareChanges,
        TResult resultIfNeverSaved)
    {
        for (var tryNumber = 1; tryNumber <= MaximumTries; tryNumber++)
        {
            var changes = await prepareChanges();

            if (!changes.HasChanges)
            {
                return changes.Result;
            }

            if (await unitOfWork.TrySaveChangesAsync())
            {
                return changes.Result;
            }
        }

        return resultIfNeverSaved;
    }
}
