namespace WalletFlow.Api.Contracts;

// Réponse au début de l'activation : le secret à saisir à la main, et le lien otpauth:// que le site transforme en code QR.
public record TwoFactorSecretResponse(string Secret, string LinkForTheAuthenticatorApplication);
