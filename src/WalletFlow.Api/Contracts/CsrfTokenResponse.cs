namespace WalletFlow.Api.Contracts;

// Le jeton anti-CSRF, à joindre à chaque requête qui modifie des données (SS §6).
public record CsrfTokenResponse(string CsrfToken);
