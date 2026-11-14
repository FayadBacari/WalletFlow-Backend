using WalletFlow.Domain.BagsAndSuggestions;

namespace WalletFlow.Application.Bags;

// Résultat d'un déplacement d'argent : toujours une seule des formes écrites dans ce fichier.
public abstract class BagMovementResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private BagMovementResult()
    {
    }

    // L'argent est déplacé.
    public sealed class Moved : BagMovementResult
    {
        public required BagMovement Movement { get; init; }
    }

    // Aucun des deux côtés n'est un sac.
    public sealed class NoBag : BagMovementResult
    {
    }

    // Les deux côtés sont le même sac.
    public sealed class SameBag : BagMovementResult
    {
    }

    // Le montant ne va pas de 1 à 1 000 000 000 centimes.
    public sealed class AmountBreaksRules : BagMovementResult
    {
    }

    // Un des sacs n'existe pas, ou il est à un autre utilisateur.
    public sealed class NotFound : BagMovementResult
    {
    }

    // L'utilisateur a déjà fait le nombre maximum de déplacements sur les dernières 24 heures.
    public sealed class TooManyMovements : BagMovementResult
    {
    }

    // Le sac de départ contient moins que ce montant : il deviendrait négatif.
    public sealed class SourceBagTooLow : BagMovementResult
    {
    }

    // L'argent disponible est plus petit que ce montant : le total des sacs dépasserait le solde total.
    public sealed class AvailableMoneyTooLow : BagMovementResult
    {
    }

    // D'autres requêtes changent les sacs sans arrêt, ou le compte n'existe plus : rien n'est enregistré.
    public sealed class NotSaved : BagMovementResult
    {
    }
}
