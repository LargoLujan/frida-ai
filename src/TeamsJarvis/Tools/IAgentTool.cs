using System.Text.Json;

namespace TeamsJarvis.Tools;

/// <summary>Tool que el agente Claude puede invocar durante el bucle agéntico.</summary>
public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    /// <summary>Propiedades del input_schema JSON (nombre → schema de la propiedad).</summary>
    IReadOnlyDictionary<string, object> SchemaProperties { get; }
    IReadOnlyList<string> RequiredProperties { get; }
    Task<string> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct);

    /// <summary>
    /// True si la tool necesita Microsoft Graph (permisos delegados del tenant). En modo
    /// "browser" (sin tocar el tenant) estas tools no funcionan y se ocultan al agente para no
    /// malgastar tokens en reintentos con 403.
    /// </summary>
    bool RequiresGraph => false;
}

public static class ToolInputExtensions
{
    public static string? GetString(this IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static int GetInt(this IReadOnlyDictionary<string, JsonElement> input, string key, int fallback) =>
        input.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : fallback;
}
