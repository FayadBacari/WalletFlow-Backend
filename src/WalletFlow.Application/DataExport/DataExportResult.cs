namespace WalletFlow.Application.DataExport;

// Résultat d'un téléchargement des données : toujours une seule des formes écrites dans ce fichier.
public abstract class DataExportResult
{
    // Constructeur privé : aucune autre forme ne peut être créée en dehors de ce fichier.
    private DataExportResult()
    {
    }

    // Les données du compte, et le moment où elles ont été lues.
    public sealed class Exported : DataExportResult
    {
        public required AccountData Data { get; init; }
        public required DateTime ExportedAt { get; init; }
    }

    // Le mot de passe n'est pas le bon, sur un compte sans double authentification.
    public sealed class WrongPassword : DataExportResult
    {
    }

    // Le mot de passe ou le code n'est pas le bon, avec la double authentification : le même refus pour les deux.
    public sealed class WrongPasswordOrCode : DataExportResult
    {
    }

    // L'utilisateur a déjà fait 3 essais dans l'heure (SS §6).
    public sealed class TooManyExports : DataExportResult
    {
    }

    // Le compte a été effacé pendant la requête.
    public sealed class AccountGone : DataExportResult
    {
    }
}
