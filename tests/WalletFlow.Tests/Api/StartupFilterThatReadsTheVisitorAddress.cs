using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace WalletFlow.Tests.Api;

// Le serveur de test ne donne pas d'adresse IP aux requêtes : ce filtre prend celle qu'un test écrit dans un en-tête.
public class StartupFilterThatReadsTheVisitorAddress : IStartupFilter
{
    public const string NameOfTheHeaderGivingTheVisitorAddress = "X-Adresse-IP-De-Test";

    // Ajoute cette lecture au tout début de l'API (nom imposé par le framework).
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> nextConfigurationOfTheApiPipeline)
    {
        return builderOfTheApiPipelineForTheTests =>
        {
            builderOfTheApiPipelineForTheTests.Use(async (contextOfTheCurrentTestRequest, nextStepOfTheApiPipeline) =>
            {
                var visitorAddressWrittenInTheHeader = contextOfTheCurrentTestRequest.Request.Headers[NameOfTheHeaderGivingTheVisitorAddress].ToString();
                if (visitorAddressWrittenInTheHeader != "")
                {
                    contextOfTheCurrentTestRequest.Connection.RemoteIpAddress = IPAddress.Parse(visitorAddressWrittenInTheHeader);
                }

                await nextStepOfTheApiPipeline();
            });

            nextConfigurationOfTheApiPipeline(builderOfTheApiPipelineForTheTests);
        };
    }
}
