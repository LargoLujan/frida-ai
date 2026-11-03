using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TeamsJarvis.Core;

namespace TeamsJarvis.Tools;

/// <summary>fabric_api: consultas GET a la API REST de Microsoft Fabric.</summary>
public sealed class FabricApiTool(GraphAuthService auth, ILogger<FabricApiTool> logger) : IAgentTool
{
    private const string BaseUrl = "https://api.fabric.microsoft.com/v1";
    private const string Scope = "https://api.fabric.microsoft.com/.default";
    private const int MaxChars = 50_000;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public string Name => "fabric_api";

    public string Description =>
        "Consulta la API REST de Microsoft Fabric (solo lectura, GET). Ejemplos de rutas: " +
        "'/workspaces' (lista workspaces), '/workspaces/{id}/items' (elementos de un workspace: lakehouses, " +
        "notebooks, pipelines...), '/workspaces/{id}/items/{itemId}/jobs/instances' (ejecuciones recientes de un item). " +
        "Úsala cuando la conversación trate de Fabric: pipelines, lakehouses, informes, errores de ejecución, capacidades.";

    public IReadOnlyDictionary<string, object> SchemaProperties => new Dictionary<string, object>
    {
        ["path"] = new
        {
            type = "string",
            description = "Ruta relativa de la API que empieza por '/', p. ej. '/workspaces'. Puede incluir query string.",
        },
    };

    public IReadOnlyList<string> RequiredProperties => ["path"];

    public async Task<string> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var path = input.GetString("path");
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
            return "Error: 'path' debe ser una ruta relativa que empiece por '/', p. ej. '/workspaces'.";

        string token;
        try
        {
            token = await auth.GetCliTokenAsync(Scope, ct);
        }
        catch (Exception ex)
        {
            return $"No se pudo obtener token de Fabric desde la sesión del az CLI (¿está hecho 'az login'?): {ex.Message}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        logger.LogInformation("fabric_api GET {Path} → {Status}.", path, (int)response.StatusCode);

        if (!response.IsSuccessStatusCode)
            return $"Fabric API devolvió {(int)response.StatusCode} {response.StatusCode}: {ContextBuilder.Truncate(body, 2000)}";

        return body.Length > MaxChars
            ? body[..MaxChars] + "\n[TRUNCADO]"
            : body;
    }
}
