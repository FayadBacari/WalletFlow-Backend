namespace WalletFlow.Application.AccountSettings;

// Résultat d'un changement du nom et du prénom : toujours une seule des formes écrites dans ce fichier.
public abstract class PersonalNameChangeResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private PersonalNameChangeResult()
    {
    }

    // Le nom et le prénom sont enregistrés, ou effacés s'ils étaient vides.
    public sealed class Changed : PersonalNameChangeResult
    {
    }

    // Le nom ou le prénom ne respecte pas les règles. Les deux refus peuvent arriver ensemble.
    public sealed class RefusedByRules : PersonalNameChangeResult
    {
        public required bool IsLastNameInvalid { get; init; }
        public required bool IsFirstNameInvalid { get; init; }
    }

    // D'autres requêtes changent le compte sans arrêt, ou il n'existe plus : rien n'est enregistré.
    public sealed class NotSaved : PersonalNameChangeResult
    {
    }
}
