namespace WalletFlow.Application.AccountRecovery;

// Résultat d'une demande de récupération : une seule de ces valeurs à la fois.
// Il ne dit jamais si un compte utilise l'adresse (SS §2).
public enum PasswordRecoveryRequestResult
{
    RequestAccepted,
    InvalidEmailAddressFormat,
    TooManyRequests
}
