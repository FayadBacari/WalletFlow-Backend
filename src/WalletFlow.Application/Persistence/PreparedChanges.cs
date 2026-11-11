namespace WalletFlow.Application.Persistence;

// Ce qu'un passage « lire, vérifier, modifier » a décidé : soit un contrôle a refusé et il n'y a rien à enregistrer,
// soit des changements sont prêts et la réponse ne vaut qu'une fois ces changements enregistrés.
public class PreparedChanges<TResult>
{
    public required bool HasChanges { get; init; }
    public required TResult Result { get; init; }

    // Un contrôle a refusé : rien n'a été modifié, et cette réponse part tout de suite.
    public static PreparedChanges<TResult> NothingToSave(TResult result)
    {
        return new PreparedChanges<TResult> { HasChanges = false, Result = result };
    }

    // Des changements sont prêts : cette réponse ne part que si leur enregistrement réussit.
    public static PreparedChanges<TResult> SaveThenReturn(TResult result)
    {
        return new PreparedChanges<TResult> { HasChanges = true, Result = result };
    }
}
