using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace WalletFlow.Api.Configuration;

// Limites de débit de l'API (SS §6). Au-delà de la limite, le serveur répond 429 « trop de requêtes ».
public static class RateLimitingConfiguration
{
    public const string PolicyNameForSignupRequestsPerIpAddress = "inscription-par-adresse-ip";

    // Inscription : 5 par heure et par adresse IP.
    public static FixedWindowRateLimiterOptions CreateOptionsOfTheSignupRequestsLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromHours(1)
        };
    }

    // Ajoute les limites de débit aux services de l'API.
    public static IServiceCollection AddWalletFlowRateLimitingToTheServices(this IServiceCollection servicesOfTheWalletFlowApplication)
    {
        servicesOfTheWalletFlowApplication.AddRateLimiter(optionsOfTheRateLimitingToConfigure =>
        {
            optionsOfTheRateLimitingToConfigure.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            optionsOfTheRateLimitingToConfigure.AddPolicy(PolicyNameForSignupRequestsPerIpAddress, httpContextOfTheRequestToLimit =>
            {
                var ipAddressOfTheRequestToLimit = httpContextOfTheRequestToLimit.Connection.RemoteIpAddress?.ToString() ?? "adresse-inconnue";

                return RateLimitPartition.GetFixedWindowLimiter(ipAddressOfTheRequestToLimit, ipAddressUsedAsThePartitionKey =>
                    CreateOptionsOfTheSignupRequestsLimit());
            });
        });

        return servicesOfTheWalletFlowApplication;
    }
}
