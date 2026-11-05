using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace WalletFlow.Api.Configuration;

// Limites de débit de l'API (SS §6). Au-delà de la limite, le serveur répond 429 « trop de requêtes ».
public static class RateLimitingConfiguration
{
    public const string PolicyNameForSignupRequestsPerIpAddress = "inscription-par-adresse-ip";
    public const string PolicyNameForConfirmationAttemptsPerIpAddress = "confirmation-par-adresse-ip";
    public const string PolicyNameForLoginAttemptsPerIpAddress = "connexion-par-adresse-ip";
    public const string PolicyNameForPasswordRecoveryRequestsPerIpAddress = "mot-de-passe-oublie-par-adresse-ip";
    public const string PolicyNameForPasswordResetAttemptsPerIpAddress = "lien-de-recuperation-par-adresse-ip";
    public const string PolicyNameForAccountUnlockAttemptsPerIpAddress = "lien-de-deblocage-par-adresse-ip";
    public const string PolicyNameForTwoFactorCodeAttemptsPerIpAddress = "code-de-double-authentification-par-adresse-ip";
    public const string PolicyNameForTwoFactorSettingsRequestsPerIpAddress = "reglage-de-la-double-authentification-par-adresse-ip";

    // Connexion : 10 essais par minute et par adresse IP, en plus des 5 tentatives par compte.
    public static FixedWindowRateLimiterOptions CreateOptionsOfTheLoginAttemptsLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1)
        };
    }

    // Inscription : 5 par heure et par adresse IP.
    public static FixedWindowRateLimiterOptions CreateOptionsOfTheSignupRequestsLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromHours(1)
        };
    }

    // Confirmation de l'inscription : 10 essais par heure et par adresse IP.
    public static FixedWindowRateLimiterOptions CreateOptionsOfTheConfirmationAttemptsLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1)
        };
    }

    // Mot de passe oublié, lien de récupération, lien de déblocage : 10 par heure et par adresse IP. Chaque route a son compteur.
    public static FixedWindowRateLimiterOptions CreateOptionsOfTheAccountRecoveryRequestsLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1)
        };
    }

    // Code de double authentification : 10 essais par minute et par adresse IP, en plus des 5 essais par connexion.
    public static FixedWindowRateLimiterOptions CreateOptionsOfTheTwoFactorCodeAttemptsLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1)
        };
    }

    // Réglage de la double authentification : 10 par heure et par adresse IP, avec un seul compteur pour ses trois routes.
    public static FixedWindowRateLimiterOptions CreateOptionsOfTheTwoFactorSettingsRequestsLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1)
        };
    }

    // Ajoute les limites de débit aux services de l'API.
    public static IServiceCollection AddWalletFlowRateLimitingToTheServices(this IServiceCollection servicesOfTheWalletFlowApplication)
    {
        servicesOfTheWalletFlowApplication.AddRateLimiter(optionsOfTheRateLimitingToConfigure =>
        {
            optionsOfTheRateLimitingToConfigure.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            AddLimitPerIpAddressToTheOptions(optionsOfTheRateLimitingToConfigure, PolicyNameForSignupRequestsPerIpAddress, CreateOptionsOfTheSignupRequestsLimit());
            AddLimitPerIpAddressToTheOptions(optionsOfTheRateLimitingToConfigure, PolicyNameForConfirmationAttemptsPerIpAddress, CreateOptionsOfTheConfirmationAttemptsLimit());
            AddLimitPerIpAddressToTheOptions(optionsOfTheRateLimitingToConfigure, PolicyNameForLoginAttemptsPerIpAddress, CreateOptionsOfTheLoginAttemptsLimit());
            AddLimitPerIpAddressToTheOptions(optionsOfTheRateLimitingToConfigure, PolicyNameForPasswordRecoveryRequestsPerIpAddress, CreateOptionsOfTheAccountRecoveryRequestsLimit());
            AddLimitPerIpAddressToTheOptions(optionsOfTheRateLimitingToConfigure, PolicyNameForPasswordResetAttemptsPerIpAddress, CreateOptionsOfTheAccountRecoveryRequestsLimit());
            AddLimitPerIpAddressToTheOptions(optionsOfTheRateLimitingToConfigure, PolicyNameForAccountUnlockAttemptsPerIpAddress, CreateOptionsOfTheAccountRecoveryRequestsLimit());
            AddLimitPerIpAddressToTheOptions(optionsOfTheRateLimitingToConfigure, PolicyNameForTwoFactorCodeAttemptsPerIpAddress, CreateOptionsOfTheTwoFactorCodeAttemptsLimit());
            AddLimitPerIpAddressToTheOptions(optionsOfTheRateLimitingToConfigure, PolicyNameForTwoFactorSettingsRequestsPerIpAddress, CreateOptionsOfTheTwoFactorSettingsRequestsLimit());
        });

        return servicesOfTheWalletFlowApplication;
    }

    // Ajoute une limite, avec un compteur par adresse IP.
    private static void AddLimitPerIpAddressToTheOptions(RateLimiterOptions optionsOfTheRateLimitingToConfigure, string nameOfTheLimitPolicyToAdd, FixedWindowRateLimiterOptions optionsOfTheLimitToAdd)
    {
        optionsOfTheRateLimitingToConfigure.AddPolicy(nameOfTheLimitPolicyToAdd, httpContextOfTheRequestToLimit =>
            RateLimitPartition.GetFixedWindowLimiter(
                ReadIpAddressOfTheRequestToLimit(httpContextOfTheRequestToLimit),
                ipAddressUsedAsThePartitionKey => optionsOfTheLimitToAdd));
    }

    // Donne l'adresse IP qui sert de compteur à une requête.
    private static string ReadIpAddressOfTheRequestToLimit(HttpContext httpContextOfTheRequestToLimit)
    {
        return httpContextOfTheRequestToLimit.Connection.RemoteIpAddress?.ToString() ?? "adresse-inconnue";
    }
}
