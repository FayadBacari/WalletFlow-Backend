namespace WalletFlow.Application.Bags;

// Résultat du changement de nom d'un sac : toujours une seule des formes écrites dans ce fichier.
public abstract class BagRenameResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private BagRenameResult()
    {
    }

    // Le sac porte son nouveau nom.
    public sealed class Renamed : BagRenameResult
    {
        public required BagView Bag { get; init; }
    }

    // Le nom ne respecte pas les règles.
    public sealed class NameBreaksRules : BagRenameResult
    {
    }

    // Le sac n'existe pas, ou il est à un autre utilisateur.
    public sealed class NotFound : BagRenameResult
    {
    }

    // D'autres requêtes changent ce sac sans arrêt : rien n'est enregistré.
    public sealed class NotSaved : BagRenameResult
    {
    }
}
