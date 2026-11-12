namespace WalletFlow.Application.AccountSettings;

// Résultat d'une demande de changement d'adresse. Une demande acceptée ne dit jamais si la nouvelle adresse était libre (SS §2).
public enum EmailChangeRequestResult
{
    RequestAccepted,
    InvalidEmailAddress,
    SameEmailAddress,
    WrongPassword,
    WrongPasswordOrCode,
    TooManyRequests
}
