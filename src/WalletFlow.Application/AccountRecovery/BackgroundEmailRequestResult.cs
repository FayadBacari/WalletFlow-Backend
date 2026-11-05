namespace WalletFlow.Application.AccountRecovery;

// Résultat du traitement d'une demande d'e-mail, pour le journal technique. Le visiteur a déjà reçu sa réponse.
public class BackgroundEmailRequestResult
{
    public bool EmailCouldNotBeSent { get; init; }
    public Guid? UserIdentifierForTheTechnicalLog { get; init; }
}
