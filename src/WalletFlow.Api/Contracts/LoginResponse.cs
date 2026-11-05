namespace WalletFlow.Api.Contracts;

// Réponse à un mot de passe accepté : la connexion est finie, ou elle attend le code de double authentification.
public record LoginResponse(string Message, bool IsTwoFactorCodeRequired);
