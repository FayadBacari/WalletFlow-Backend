namespace WalletFlow.Application.AccountRecovery;

// Résultat d'une demande de récupération : acceptée, ou refusée avec la raison du refus.
public class PasswordRecoveryRequestResult
{
    public string? EmailAddressErrorMessage { get; init; }
    public bool TooManyRequestsForThisEmailAddress { get; init; }

    public bool IsAccepted => EmailAddressErrorMessage == null && !TooManyRequestsForThisEmailAddress;
}
