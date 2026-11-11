namespace WalletFlow.Tests.Api;

// Valeurs et outils communs aux tests de l'inscription et de la confirmation.
public static class RegistrationRequestTestValues
{
    public const string RegistrationEndpoint = "/api/account/register";
    public const string ConfirmationEndpoint = "/api/account/confirm-email";
    public const string ValidPassword = "Mot-De-Passe-Valide-1";

    // Retrouve le jeton écrit dans le lien de l'e-mail de confirmation.
    public static string ReadConfirmationToken(string body)
    {
        var textAfterMarker = body.Split("#token=")[1];

        return textAfterMarker.Split('\n')[0].Trim();
    }
}
