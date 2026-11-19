using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using WalletFlow.Api.Configuration;
using static WalletFlow.Tests.Api.LoginRequestTestValues;

namespace WalletFlow.Tests.Api;

// Vérifie le document OpenAPI tel que le site le lit en développement : chaque route annonce sa réussite et tous ses refus, au
// format « problem details », en français (SS §10). Chaque test de l'API vérifie aussi que ses réponses y sont décrites
// (voir ResponseContractCheck).
public class OpenApiDocumentTests
{
    // Les routes appelées sans session (voir UserSessionEndpointTests) : seules la connexion et son code répondent quand même 401.
    private static readonly List<string> PublicPaths = new List<string>
    {
        "/api/account/confirm-email", "/api/account/confirm-email-change", "/api/account/disable-two-factor",
        "/api/account/forgot-password", "/api/account/login", "/api/account/login/two-factor", "/api/account/register",
        "/api/account/reset-password", "/api/account/unlock", "/api/support-requests/visitor"
    };

    // Une opération du document : sa méthode, son adresse et sa description.
    private sealed record Operation(string Method, string Path, JsonElement Content)
    {
        public JsonElement Responses => Content.GetProperty("responses");

        public List<int> Codes => Responses.EnumerateObject().Select(response => int.Parse(response.Name, CultureInfo.InvariantCulture)).ToList();
    }

    // Lit le document publié en développement.
    private static async Task<JsonElement> ReadDocumentAsync()
    {
        using var api = new ApiFactoryInDevelopment();
        var client = api.CreateClient();

        return JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json")).RootElement;
    }

    // Donne toutes les opérations du document.
    private static async Task<List<Operation>> ReadOperationsAsync()
    {
        var document = await ReadDocumentAsync();
        var operations = new List<Operation>();

        foreach (var path in document.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                operations.Add(new Operation(operation.Name, path.Name, operation.Value));
            }
        }

        return operations;
    }

    // Donne le nom du schéma visé par un lien « $ref ».
    private static string ReadSchemaName(JsonElement schema)
    {
        return schema.GetProperty("$ref").GetString()!.Replace("#/components/schemas/", "");
    }

    [Fact]
    public async Task EveryOperationAnnouncesOneSuccessWithItsJsonSchema()
    {
        var operations = await ReadOperationsAsync();

        foreach (var operation in operations)
        {
            var success = Assert.Single(operation.Codes, code => code < 400);

            if (success == 204)
            {
                Assert.False(operation.Responses.GetProperty("204").TryGetProperty("content", out _));
                continue;
            }

            var content = Assert.Single(operation.Responses.GetProperty(success.ToString(CultureInfo.InvariantCulture)).GetProperty("content").EnumerateObject());
            Assert.Equal("application/json", content.Name);
            var schema = content.Value.GetProperty("schema");
            Assert.NotEmpty(ReadSchemaName(schema.TryGetProperty("items", out var items) ? items : schema));
        }

        Assert.Equal(64, operations.Count);
    }

    // Une erreur est toujours un « problem details », avec les messages de chaque champ pour une requête refusée.
    [Fact]
    public async Task EveryErrorIsAProblemDescribedInFrench()
    {
        foreach (var operation in await ReadOperationsAsync())
        {
            foreach (var response in operation.Responses.EnumerateObject())
            {
                var statusCode = int.Parse(response.Name, CultureInfo.InvariantCulture);
                var description = response.Value.GetProperty("description").GetString();
                Assert.False(string.IsNullOrEmpty(description));
                Assert.NotEqual(ReasonPhrases.GetReasonPhrase(statusCode), description);

                if (statusCode >= 400)
                {
                    var content = Assert.Single(response.Value.GetProperty("content").EnumerateObject());
                    Assert.Equal("application/problem+json", content.Name);
                    Assert.Equal(statusCode == 400 ? "HttpValidationProblemDetails" : "ProblemDetails", ReadSchemaName(content.Value.GetProperty("schema")));
                }
            }
        }
    }

    // Les refus de la chaîne commune sont annoncés là où ils peuvent arriver, et seulement là.
    [Fact]
    public async Task EveryOperationAnnouncesTheRefusalsOfTheCommonChain()
    {
        foreach (var operation in await ReadOperationsAsync())
        {
            var codes = operation.Codes;
            var isPublic = PublicPaths.Contains(operation.Path);
            var isWrite = operation.Method != "get";
            var where = operation.Method + " " + operation.Path;

            Assert.True(codes.Contains(429) && codes.Contains(500), where);
            var retryAfter = operation.Responses.GetProperty("429").GetProperty("headers").GetProperty("Retry-After");
            Assert.True(retryAfter.GetProperty("required").GetBoolean(), where);
            Assert.Equal("integer", retryAfter.GetProperty("schema").GetProperty("type").GetString());

            Assert.True(codes.Contains(401) == (!isPublic || operation.Path.StartsWith("/api/account/login", StringComparison.Ordinal)), where);
            Assert.True(codes.Contains(403) == (operation.Path.StartsWith("/api/admin/", StringComparison.Ordinal) || operation.Path == "/api/account/login"), where);
            Assert.True(codes.Contains(413) == isWrite, where);
            Assert.True(codes.Contains(415) == operation.Content.TryGetProperty("requestBody", out _), where);

            if (isWrite)
            {
                var refusal = operation.Responses.GetProperty("400").GetProperty("description").GetString()!;
                Assert.True(refusal.Contains("csrfToken", StringComparison.Ordinal) == !isPublic, where);
            }
        }
    }

    // Chaque route représentative annonce exactement les réponses qu'elle peut donner.
    [Theory]
    [InlineData("post", "/api/categories", "201 400 401 409 413 415 429 500")]
    [InlineData("get", "/api/categories", "200 401 429 500")]
    [InlineData("get", "/api/transactions", "200 400 401 429 500")]
    [InlineData("post", "/api/account/login", "200 400 401 403 413 415 429 500 503")]
    [InlineData("post", "/api/account/register", "200 400 413 415 429 500 503")]
    [InlineData("post", "/api/account/logout", "200 400 401 413 429 500")]
    [InlineData("post", "/api/suggestions/{suggestionId}/ignore", "204 400 401 404 409 413 429 500")]
    [InlineData("post", "/api/admin/accounts/{accountId}/unlock", "200 400 401 403 404 409 413 429 500")]
    [InlineData("get", "/api/banks", "200 401 429 500 503")]
    public async Task OperationAnnouncesExactlyItsResponses(string method, string path, string expectedCodes)
    {
        var operation = (await ReadOperationsAsync()).Single(candidate => candidate.Method == method && candidate.Path == path);

        Assert.Equal(expectedCodes, string.Join(" ", operation.Codes.Order()));
    }

    // La réussite nomme le vrai type de la réponse, y compris une liste.
    [Theory]
    [InlineData("get", "/api/categories", "200", "CategoryResponse")]
    [InlineData("post", "/api/bags/movements", "201", "BagMovementResponse")]
    [InlineData("post", "/api/suggestions/{suggestionId}/accept", "201", "BagMovementResponse")]
    [InlineData("post", "/api/account/export", "200", "DataExportFile")]
    [InlineData("post", "/api/account/login/two-factor", "200", "LoginResponse")]
    public async Task SuccessNamesItsRealType(string method, string path, string statusCode, string schemaName)
    {
        var operation = (await ReadOperationsAsync()).Single(candidate => candidate.Method == method && candidate.Path == path);

        var schema = operation.Responses.GetProperty(statusCode).GetProperty("content").GetProperty("application/json").GetProperty("schema");

        Assert.Equal(schemaName, ReadSchemaName(schema.TryGetProperty("items", out var items) ? items : schema));
    }

    // Un refus propre à une route garde sa description précise, et l'en-tête Retry-After n'est promis que là où il est toujours là.
    [Fact]
    public async Task OwnRefusalsKeepTheirPreciseDescription()
    {
        var operations = await ReadOperationsAsync();
        var login = operations.Single(operation => operation.Method == "post" && operation.Path == "/api/account/login").Responses;
        var banks = operations.Single(operation => operation.Method == "get" && operation.Path == "/api/banks").Responses;

        Assert.Equal(OpenApiConfiguration.WrongCredentialsDescription, login.GetProperty("401").GetProperty("description").GetString());
        Assert.Equal(OpenApiConfiguration.EmailNotConfirmedDescription, login.GetProperty("403").GetProperty("description").GetString());
        Assert.Equal(OpenApiConfiguration.ServerBusyDescription, login.GetProperty("503").GetProperty("description").GetString());
        Assert.Equal(OpenApiConfiguration.BankUnavailableDescription, banks.GetProperty("503").GetProperty("description").GetString());
        var busyRetry = login.GetProperty("503").GetProperty("headers").GetProperty("Retry-After");
        Assert.False(busyRetry.TryGetProperty("required", out var required) && required.GetBoolean());
    }

    // Donne les valeurs qu'une opération lit après le « ? ».
    private static List<JsonElement> ReadQueryValues(Operation operation)
    {
        if (!operation.Content.TryGetProperty("parameters", out var parameters))
        {
            return new List<JsonElement>();
        }

        return parameters.EnumerateArray().Where(parameter => parameter.GetProperty("in").GetString() == "query").ToList();
    }

    // Les valeurs de l'adresse portent le nom de leur champ JSON, comme le site les écrit.
    [Fact]
    public async Task QueryValuesAreNamedLikeTheJson()
    {
        var operations = await ReadOperationsAsync();
        var search = operations.Single(operation => operation.Method == "get" && operation.Path == "/api/transactions");

        Assert.Equal("text amountInCents from to accountId categoryId page", string.Join(" ", ReadQueryValues(search).Select(value => value.GetProperty("name").GetString())));

        foreach (var operation in operations)
        {
            foreach (var value in ReadQueryValues(operation))
            {
                Assert.True(char.IsAsciiLetterLower(value.GetProperty("name").GetString()![0]), operation.Path);
            }
        }
    }

    // Le nom qu'OpenAPI donne à une valeur de l'adresse est celui que l'API lit, et celui de la clé de son erreur : chaque valeur
    // typée (date, nombre, identifiant), envoyée illisible sous ce nom, est refusée sous ce même nom.
    [Fact]
    public async Task EachQueryValueIsRefusedUnderItsDocumentedName()
    {
        var operations = await ReadOperationsAsync();
        using var api = new ApiFactoryWithFakes();
        await AddAccountAsync(api);
        var client = CreateBrowserClient(api);
        await SendLoginRequestAsync(client);
        var checkedCount = 0;

        foreach (var operation in operations.Where(operation => operation.Method == "get"))
        {
            var address = operation.Path.Replace("{bagId}", Guid.NewGuid().ToString(), StringComparison.Ordinal);

            foreach (var value in ReadQueryValues(operation).Where(value => value.GetProperty("schema").TryGetProperty("format", out _)))
            {
                var name = value.GetProperty("name").GetString()!;
                var response = await client.GetAsync(address + "?" + name + "=illisible");

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
                Assert.True(errors.TryGetProperty(name, out _), operation.Path + " : " + name);
                checkedCount = checkedCount + 1;
            }
        }

        Assert.True(checkedCount >= 14, "Seules " + checkedCount + " valeurs ont été essayées : le test ne regarde plus les bonnes.");
    }

    // SS §5 : un montant s'écrit comme un nombre JSON. Le document ne doit annoncer nulle part qu'un texte est accepté à sa place.
    [Fact]
    public async Task NumbersAreNeverAnnouncedAsText()
    {
        var document = await ReadDocumentAsync();
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var numbersAsText = new List<string>();

        foreach (var schema in schemas.EnumerateObject())
        {
            foreach (var property in schema.Value.TryGetProperty("properties", out var properties) ? properties.EnumerateObject() : Enumerable.Empty<JsonProperty>())
            {
                var text = property.Value.GetRawText();

                if (text.Contains("\"string\"", StringComparison.Ordinal) && (text.Contains("\"integer\"", StringComparison.Ordinal) || text.Contains("\"number\"", StringComparison.Ordinal)))
                {
                    numbersAsText.Add(schema.Name + "." + property.Name);
                }
            }
        }

        Assert.Empty(numbersAsText);
        var amount = schemas.GetProperty("BagMovementRequest").GetProperty("properties").GetProperty("amountInCents");
        Assert.Equal("integer", amount.GetProperty("type").GetString());
    }
}
