namespace WalletFlow.Tests.Api;

// Valeurs et outils communs aux tests de l'inscription et de la confirmation.
public static class RegistrationRequestTestValues
{
    public const string AddressOfTheAccountRegistrationEndpoint = "/api/account/register";
    public const string AddressOfTheAccountConfirmationEndpoint = "/api/account/confirm-email";
    public const string ValidPasswordChosenInTheseTests = "Mot-De-Passe-Valide-1";

    // Retrouve le jeton écrit dans le lien de l'e-mail de confirmation.
    public static string ReadTokenWrittenInTheConfirmationLink(string bodyOfTheSignupConfirmationEmail)
    {
        var textWrittenAfterTheTokenMarker = bodyOfTheSignupConfirmationEmail.Split("#token=")[1];

        return textWrittenAfterTheTokenMarker.Split('\n')[0].Trim();
    }
}
