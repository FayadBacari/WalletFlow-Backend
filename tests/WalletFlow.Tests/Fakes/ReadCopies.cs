namespace WalletFlow.Tests.Fakes;

// Copies des objets lus depuis le dernier enregistrement, comme le suivi des objets d'EF Core :
// si l'enregistrement échoue, chaque objet lu reprend les valeurs qu'il avait à sa lecture.
// Les faux dépôts appellent ces méthodes en gardant leur propre verrou.
public class ReadCopies<TEntity> where TEntity : class
{
    private readonly Dictionary<TEntity, TEntity> copies = new Dictionary<TEntity, TEntity>();

    // Garde une copie de l'objet tel qu'il est lu. Chaque lecture renouvelle la copie :
    // aucun service ne relit un objet après l'avoir changé dans le même passage.
    public void Keep(TEntity entity)
    {
        copies[entity] = EntityValues.Copy(entity);
    }

    // Remet chaque objet lu comme il était à sa lecture, comme une transaction annulée.
    public void RestoreAll()
    {
        foreach (var entry in copies)
        {
            EntityValues.Restore(entry.Value, entry.Key);
        }
    }

    // Donne le premier objet lu qui a changé de cette façon depuis sa lecture, ou null.
    // La fonction reçoit la copie faite à la lecture, puis l'objet tel qu'il est maintenant.
    public TEntity? FindChangedSinceRead(Func<TEntity, TEntity, bool> hasChanged)
    {
        foreach (var entry in copies)
        {
            if (hasChanged(entry.Value, entry.Key))
            {
                return entry.Key;
            }
        }

        return null;
    }

    // Oublie les copies : le prochain passage repart de ce qui est gardé.
    public void Clear()
    {
        copies.Clear();
    }
}
