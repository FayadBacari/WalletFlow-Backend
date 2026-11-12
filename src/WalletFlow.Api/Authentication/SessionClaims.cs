using System.Security.Claims;

namespace WalletFlow.Api.Authentication;

// Lit l'identité que le gestionnaire des sessions a donnée à la requête (voir UserSessionAuthenticationHandler).
public static class SessionClaims
{
    // Donne l'identifiant de l'utilisateur connecté. Il vient toujours de la session, jamais de la requête (SS §4).
    public static Guid ReadUserId(ClaimsPrincipal user)
    {
        var claimValue = user.FindFirstValue(ClaimTypes.NameIdentifier);

        // Le gestionnaire des sessions écrit toujours cet identifiant : sans lui, la requête n'aurait pas passé l'authentification.
        if (claimValue == null)
        {
            throw new InvalidOperationException("La session ne donne pas l'identifiant de son propriétaire.");
        }

        return Guid.Parse(claimValue);
    }
}
