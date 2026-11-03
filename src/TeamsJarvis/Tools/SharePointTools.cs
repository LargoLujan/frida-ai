using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;
using Microsoft.Graph.Search.Query;
using TeamsJarvis.Core;

namespace TeamsJarvis.Tools;

/// <summary>search_sharepoint: búsqueda de documentos/sitios en el tenant vía Graph Search.</summary>
public sealed class SharePointSearchTool(GraphAuthService auth, ILogger<SharePointSearchTool> logger) : IAgentTool
{
    public string Name => "search_sharepoint";
    public bool RequiresGraph => true;

    public string Description =>
        "Busca documentos, archivos y páginas en SharePoint y OneDrive del tenant. " +
        "Úsala cuando la conversación mencione documentos, informes, procedimientos o datos que puedan estar en SharePoint. " +
        "Devuelve nombre, resumen, URL y los identificadores (drive_id, item_id) necesarios para leer el archivo con read_sharepoint_file.";

    public IReadOnlyDictionary<string, object> SchemaProperties => new Dictionary<string, object>
    {
        ["query"] = new { type = "string", description = "Términos de búsqueda (palabras clave, nombre del documento...)" },
    };

    public IReadOnlyList<string> RequiredProperties => ["query"];

    public async Task<string> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var query = input.GetString("query");
        if (string.IsNullOrWhiteSpace(query)) return "Error: falta el parámetro 'query'.";

        var graph = await auth.GetGraphClientAsync(ct);
        var response = await graph.Search.Query.PostAsQueryPostResponseAsync(new QueryPostRequestBody
        {
            Requests =
            [
                new SearchRequest
                {
                    EntityTypes = [EntityType.DriveItem, EntityType.ListItem, EntityType.Site],
                    Query = new SearchQuery { QueryString = query },
                    Size = 10,
                },
            ],
        }, cancellationToken: ct);

        var sb = new StringBuilder();
        var count = 0;
        foreach (var container in response?.Value?.SelectMany(v => v.HitsContainers ?? []) ?? [])
        {
            foreach (var hit in container.Hits ?? [])
            {
                count++;
                sb.AppendLine($"Resultado {count}:");
                sb.AppendLine($"  resumen: {TeamsService.HtmlToPlainText(hit.Summary ?? "")}");
                switch (hit.Resource)
                {
                    case DriveItem item:
                        sb.AppendLine($"  tipo: archivo | nombre: {item.Name}");
                        sb.AppendLine($"  url: {item.WebUrl}");
                        sb.AppendLine($"  drive_id: {item.ParentReference?.DriveId} | item_id: {item.Id}");
                        break;
                    case Site site:
                        sb.AppendLine($"  tipo: sitio | nombre: {site.DisplayName ?? site.Name}");
                        sb.AppendLine($"  url: {site.WebUrl}");
                        break;
                    default:
                        sb.AppendLine($"  tipo: {hit.Resource?.OdataType}");
                        break;
                }
                sb.AppendLine();
            }
        }

        if (count == 0) return $"Sin resultados en SharePoint/OneDrive para: {query}";
        logger.LogInformation("search_sharepoint('{Query}') → {Count} resultados.", query, count);
        return sb.ToString();
    }
}

/// <summary>read_sharepoint_file: descarga y extrae el texto de un archivo encontrado con search_sharepoint.</summary>
public sealed class SharePointReadTool(GraphAuthService auth, ILogger<SharePointReadTool> logger) : IAgentTool
{
    private const int MaxChars = 60_000;

    public string Name => "read_sharepoint_file";
    public bool RequiresGraph => true;

    public string Description =>
        "Lee el contenido de un archivo de SharePoint/OneDrive usando el drive_id e item_id devueltos por search_sharepoint. " +
        "Soporta txt, md, csv, json, log, docx y xlsx. PDF no está soportado todavía.";

    public IReadOnlyDictionary<string, object> SchemaProperties => new Dictionary<string, object>
    {
        ["drive_id"] = new { type = "string", description = "drive_id del resultado de búsqueda" },
        ["item_id"] = new { type = "string", description = "item_id del resultado de búsqueda" },
    };

    public IReadOnlyList<string> RequiredProperties => ["drive_id", "item_id"];

    public async Task<string> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
    {
        var driveId = input.GetString("drive_id");
        var itemId = input.GetString("item_id");
        if (string.IsNullOrWhiteSpace(driveId) || string.IsNullOrWhiteSpace(itemId))
            return "Error: faltan 'drive_id' o 'item_id'.";

        var graph = await auth.GetGraphClientAsync(ct);
        var item = await graph.Drives[driveId].Items[itemId].GetAsync(cancellationToken: ct);
        var name = item?.Name ?? "(sin nombre)";
        var extension = Path.GetExtension(name).ToLowerInvariant();

        await using var stream = await graph.Drives[driveId].Items[itemId].Content.GetAsync(cancellationToken: ct);
        if (stream is null) return $"No se pudo descargar el archivo {name}.";

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);
        memory.Position = 0;

        if (!DocumentExtractor.IsSupported(extension))
            return $"El formato '{extension}' de {name} no está soportado (soportados: texto plano, docx y xlsx).";

        string text;
        try { text = DocumentExtractor.ExtractFromStream(memory, extension); }
        catch (Exception ex) { return $"No se pudo leer {name}: {ex.Message}"; }

        logger.LogInformation("read_sharepoint_file('{Name}') → {Chars} caracteres.", name, text.Length);
        if (text.Length > MaxChars)
            text = text[..MaxChars] + $"\n\n[TRUNCADO: el archivo continúa, {text.Length} caracteres en total]";
        return $"Contenido de {name}:\n\n{text}";
    }
}
