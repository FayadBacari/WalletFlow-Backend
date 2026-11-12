namespace WalletFlow.Api.Contracts;

// Réponse à la lecture du profil : ce que l'utilisateur connecté voit de son compte. Aucun secret n'en fait partie.
public record AccountProfileResponse(string Email, string? LastName, string? FirstName, bool IsTwoFactorEnabled);
