using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace WalletFlow.Api.Configuration;

// Limites de débit de l'API (SS §6). Au-delà de la limite, le serveur répond 429 « trop de requêtes ». Toute requête compte
// d'abord dans la limite globale de son adresse IP, puis dans la limite de sa route s'il y en a une.
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
    public const string VisitorSupportPolicy = "support-visiteur-par-adresse-ip";
    public const string TwoFactorSupportLinkPolicy = "lien-de-desactivation-de-la-double-auth-par-adresse-ip";
    public const string AccountDeletionPolicy = "suppression-du-compte-par-adresse-ip";
    public const string BankPolicy = "banque-par-session";

    // Toutes les requêtes : 600 par minute et par adresse IP. Une limite large, bien au-dessus d'un usage normal : elle arrête
    // un robot qui frappe partout, y compris les routes ouvertes à tous et les cookies de session inventés, que la base doit
    // chercher avant de les refuser.
    public static FixedWindowRateLimiterOptions CreateGlobalLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 600,
            Window = TimeSpan.FromMinutes(1)
        };
    }

    // Routes de la banque qui appellent le prestataire : la liste des banques, la connexion, son renouvellement, sa fin et la
    // déconnexion, 20 par heure et par session, toutes ensemble. Le prestataire limite les appels de toute l'application : un
    // seul compte ne doit pas pouvoir épuiser cette limite pour tous les autres.
    public static FixedWindowRateLimiterOptions CreateBankLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromHours(1)
        };
    }

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

    // Mot de passe oublié, lien de récupération, lien de déblocage, lien de désactivation de la double authentification :
    // 10 par heure et par adresse IP. Chaque route a son compteur.
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

    // Changement de mot de passe, et demande de suppression du compte : 10 par heure et par adresse IP, chacun son compteur.
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

    // Formulaire de support des visiteurs : 3 demandes par heure et par adresse IP. Il est ouvert à tous : sans limite,
    // un script remplirait la base de demandes.
    public static FixedWindowRateLimiterOptions CreateVisitorSupportLimit()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromHours(1)
        };
    }

    // Ajoute les limites de débit aux services de l'API.
    public static IServiceCollection AddWalletFlowRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Le refus dit quand réessayer : à la fin de la fenêtre de la limite atteinte. Le corps de la réponse est écrit
            // ensuite, au format de toutes les erreurs.
            options.OnRejected = (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay))
                {
                    context.HttpContext.Response.Headers.RetryAfter = RetryDelays.InSeconds(delay);
                }

                return ValueTask.CompletedTask;
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ReadIpAddress(httpContext),
                    ipAddress => CreateGlobalLimit()));

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
            AddLimitPerIp(options, VisitorSupportPolicy, CreateVisitorSupportLimit());
            AddLimitPerIp(options, TwoFactorSupportLinkPolicy, CreateAccountRecoveryLimit());
            AddLimitPerIp(options, AccountDeletionPolicy, CreatePasswordChangeLimit());
            AddLimitPerSession(options, BankPolicy, CreateBankLimit());
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
        return FindCounterKey(httpContext.Connection.RemoteIpAddress);
    }

    // Donne le compteur d'une adresse IP. Une adresse IPv4 vue en IPv6 (::ffff:203.0.113.7) redevient une adresse IPv4 :
    // sinon, toutes les adresses IPv4 tomberaient dans le même bloc. Une adresse IPv6 compte pour tout son bloc /64 : un
    // abonné en reçoit un entier, et pourrait changer d'adresse à chaque requête pour échapper aux limites.
    public static string FindCounterKey(IPAddress? address)
    {
        if (address == null)
        {
            return "adresse-inconnue";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);

        return new IPAddress(bytes) + "/64";
    }
}
