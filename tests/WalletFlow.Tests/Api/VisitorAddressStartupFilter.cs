using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace WalletFlow.Tests.Api;

// Le serveur de test ne donne pas d'adresse IP aux requêtes : ce filtre prend celle qu'un test écrit dans un en-tête.
public class VisitorAddressStartupFilter : IStartupFilter
{
    public const string VisitorAddressHeader = "X-Adresse-IP-De-Test";

    // Ajoute cette lecture au tout début de l'API (nom imposé par le framework).
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, next) =>
            {
                var visitorAddress = context.Request.Headers[VisitorAddressHeader].ToString();
                if (visitorAddress != "")
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(visitorAddress);
                }

                await next();
            });

            next(app);
        };
    }
}
