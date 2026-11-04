namespace WalletFlow.Tests.Api;

// Vérifie les en-têtes de sécurité d'une réponse, avec les valeurs exactes de SS §6.
public static class SecurityHeadersAssertions
{
    // Vérifie que la réponse porte les quatre en-têtes écrits sur chaque réponse de l'API.
    public static void CheckThatAnswerCarriesTheExactSecurityHeaders(HttpResponseMessage answerToCheckForSecurityHeaders)
    {
        CheckThatHeaderOfTheAnswerHasThisExactValue(
            answerToCheckForSecurityHeaders,
            "Content-Security-Policy",
            "default-src 'self'; script-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'");
        CheckThatHeaderOfTheAnswerHasThisExactValue(answerToCheckForSecurityHeaders, "X-Content-Type-Options", "nosniff");
        CheckThatHeaderOfTheAnswerHasThisExactValue(answerToCheckForSecurityHeaders, "Referrer-Policy", "strict-origin-when-cross-origin");
        CheckThatHeaderOfTheAnswerHasThisExactValue(answerToCheckForSecurityHeaders, "Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    }

    // Vérifie qu'un en-tête est présent une seule fois, avec la valeur attendue.
    public static void CheckThatHeaderOfTheAnswerHasThisExactValue(
        HttpResponseMessage answerToCheckForThisHeader,
        string nameOfTheHeaderToCheckInTheAnswer,
        string valueExpectedForTheHeaderInTheAnswer)
    {
        var valuesOfTheHeaderFoundInTheAnswer = answerToCheckForThisHeader.Headers.GetValues(nameOfTheHeaderToCheckInTheAnswer);

        Assert.Equal(valueExpectedForTheHeaderInTheAnswer, Assert.Single(valuesOfTheHeaderFoundInTheAnswer));
    }
}
