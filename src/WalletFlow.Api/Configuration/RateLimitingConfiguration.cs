using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace WalletFlow.Api.Configuration;

// Limites de débit de l'API (SS §6). Au-delà de la limite, le serveur répond 429 « trop de requêtes ».
public static class RateLimitingConfiguration
{
    public const string SignupPolicy = "inscription-par-adresse-ip";
    public const string ConfirmationPolicy = "confirmation-par-adresse-ip";
    public const string LoginPolicy = "connexion-par-adresse-ip";
    public const string PasswordRecoveryPolicy = "mot-de-passe-oublie-par-adresse-ip";
    public const string PasswordResetPolicy = "lien-de-recuperation-par-adresse-ip";
    public const string AccountUnlockPolicy = "lien-de-deblocage-par-adresse-ip";
    public const string TwoFactorCodePolicy = "code-de-double-authentification-par-adresse-ip";
    public const string TwoFactorSettingsPolicy = "reglage-de-la-double-authentification-par-adresse-ip";
    public const string PasswordChangePolicy = "changement-de-mot-de-passe-par-adresse-ip";
    public const string EmailChangePolicy = "changement-d-adresse-par-adresse-ip";
    public const string EmailChangeLinkPolicy = "lien-de-changement-d-adresse-par-adresse-ip";
    public const string CostlyReadPolicy = "lecture-couteuse-par-session";

    // Connexion : 10 essais par minute et par adresse IP, en plus des 5 tentatives par compte.
    public static FixedWindowRateLimiterOptions CreateLoginLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1)
        };
    }

    // Inscription : 5 par heure et par adresse IP.
    public static FixedWindowRateLimiterOptions CreateSignupLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromHours(1)
        };
    }

    // Confirmation de l'inscription : 10 essais par heure et par adresse IP.
    public static FixedWindowRateLimiterOptions CreateConfirmationLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1)
        };
    }

    // Mot de passe oublié, lien de récupération, lien de déblocage : 10 par heure et par adresse IP. Chaque route a son compteur.
    public static FixedWindowRateLimiterOptions CreateAccountRecoveryLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1)
        };
    }

    // Code de double authentification : 10 essais par minute et par adresse IP, en plus des 5 essais par connexion.
    public static FixedWindowRateLimiterOptions CreateTwoFactorCodeLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1)
        };
    }

    // Réglage de la double authentification : 10 par heure et par adresse IP, avec un seul compteur pour ses trois routes.
    public static FixedWindowRateLimiterOptions CreateTwoFactorSettingsLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1)
        };
    }

    // Changement de mot de passe : 10 par heure et par adresse IP.
    public static FixedWindowRateLimiterOptions CreatePasswordChangeLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1)
        };
    }

    // Changement d'adresse e-mail : 10 par heure et par adresse IP, en plus des 3 par heure et par nouvelle adresse.
    public static FixedWindowRateLimiterOptions CreateEmailChangeLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1)
        };
    }

    // Lien de changement d'adresse : 10 essais par heure et par adresse IP.
    public static FixedWindowRateLimiterOptions CreateEmailChangeLinkLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1)
        };
    }

    // Recherche d'opérations, graphiques, liste des sacs et historique d'un sac : 60 par minute et par session, toutes ces routes
    // ensemble. Chacune additionne beaucoup de lignes : sans limite, un seul compte pourrait charger la base en boucle.
    public static FixedWindowRateLimiterOptions CreateCostlyReadLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1)
        };
    }

    // Ajoute les limites de débit aux services de l'API.
    public static IServiceCollection AddWalletFlowRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            AddLimitPerIp(options, SignupPolicy, CreateSignupLimit());
            AddLimitPerIp(options, ConfirmationPolicy, CreateConfirmationLimit());
            AddLimitPerIp(options, LoginPolicy, CreateLoginLimit());
            AddLimitPerIp(options, PasswordRecoveryPolicy, CreateAccountRecoveryLimit());
            AddLimitPerIp(options, PasswordResetPolicy, CreateAccountRecoveryLimit());
            AddLimitPerIp(options, AccountUnlockPolicy, CreateAccountRecoveryLimit());
            AddLimitPerIp(options, TwoFactorCodePolicy, CreateTwoFactorCodeLimit());
            AddLimitPerIp(options, TwoFactorSettingsPolicy, CreateTwoFactorSettingsLimit());
            AddLimitPerIp(options, PasswordChangePolicy, CreatePasswordChangeLimit());
            AddLimitPerIp(options, EmailChangePolicy, CreateEmailChangeLimit());
            AddLimitPerIp(options, EmailChangeLinkPolicy, CreateEmailChangeLinkLimit());
            AddLimitPerSession(options, CostlyReadPolicy, CreateCostlyReadLimit());
        });

        return services;
    }

    // Ajoute une limite, avec un compteur par adresse IP.
    private static void AddLimitPerIp(RateLimiterOptions options, string policyName, FixedWindowRateLimiterOptions limit)
    {
        options.AddPolicy(policyName, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                ReadIpAddress(httpContext),
                ipAddress => limit));
    }

    // Ajoute une limite, avec un compteur par session : elle suit l'utilisateur connecté, même s'il change d'adresse IP.
    // Le cookie est lu sans la base : la limite passe avant toute lecture, comme les autres.
    private static void AddLimitPerSession(RateLimiterOptions options, string policyName, FixedWindowRateLimiterOptions limit)
    {
        options.AddPolicy(policyName, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                ReadSessionKey(httpContext),
                sessionKey => limit));
    }

    // Donne l'empreinte SHA-256 du cookie de session : le compteur ne garde jamais le jeton lui-même en mémoire.
    // Sans cookie, la requête sera refusée plus loin (401) : elle compte avec son adresse IP.
    private static string ReadSessionKey(HttpContext httpContext)
    {
        var sessionToken = httpContext.Request.Cookies[SessionCookiesConfiguration.SessionCookieName];

        if (string.IsNullOrEmpty(sessionToken))
        {
            return "adresse-ip:" + ReadIpAddress(httpContext);
        }

        return "session:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sessionToken)));
    }

    // Donne l'adresse IP qui sert de compteur à une requête.
    private static string ReadIpAddress(HttpContext httpContext)
    {
        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "adresse-inconnue";
    }
}
