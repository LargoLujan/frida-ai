using System.Text.Json;

namespace TeamsJarvis.Core;

/// <summary>
/// Parsea la configuración de servidores MCP de forma tolerante, aceptando los formatos
/// habituales del ecosistema para que el usuario pegue lo que ya use en Claude Desktop/Cursor:
///
///   1) Estándar:  {"mcpServers": {"nombre": {"command": "...", "args": [...], "env": {...}, "url": "..."}}}
///   2) Mapa suelto: {"nombre": {"command": "...", ...}}
///   3) Array nativo de la app: [{"Name": "...", "Type": "stdio", "Command": "...", "Arguments": [...]}]
///
/// El tipo se infiere si no se indica: hay "url" → http, si no → stdio.
/// </summary>
public static class McpConfigParser
{
    /// <summary>Parsea el texto a la lista interna. Lanza <see cref="FormatException"/> si no es válido.</summary>
    public static List<McpServerOptions> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new FormatException($"JSON no válido: {ex.Message}"); }

        using (doc)
        {
            var root = doc.RootElement;

            // 3) Array nativo.
            if (root.ValueKind == JsonValueKind.Array)
                return root.EnumerateArray().Select(ParseArrayItem).Where(s => s is not null).Select(s => s!).ToList();

            if (root.ValueKind != JsonValueKind.Object)
                throw new FormatException("Se esperaba un objeto {\"mcpServers\": {...}} o un array de servidores.");

            // 1) Estándar con "mcpServers".
            var map = root.TryGetProperty("mcpServers", out var servers) && servers.ValueKind == JsonValueKind.Object
                ? servers
                : root; // 2) mapa suelto {nombre: {...}}

            var result = new List<McpServerOptions>();
            foreach (var entry in map.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                var opt = ParseServer(entry.Name, entry.Value);
                if (opt is not null) result.Add(opt);
            }
            return result;
        }
    }

    /// <summary>Serializa al formato estándar {"mcpServers": {...}} para mostrarlo en la UI.</summary>
    public static string ToStandardJson(IReadOnlyList<McpServerOptions> servers)
    {
        if (servers.Count == 0) return "";
        var map = new Dictionary<string, object>();
        foreach (var s in servers)
        {
            var entry = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(s.Command)) entry["command"] = s.Command;
            if (s.Arguments.Count > 0) entry["args"] = s.Arguments;
            if (!string.IsNullOrWhiteSpace(s.Url)) entry["url"] = s.Url;
            if (s.Env.Count > 0) entry["env"] = s.Env;
            map[s.Name] = entry;
        }
        return JsonSerializer.Serialize(new { mcpServers = map },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static McpServerOptions? ParseServer(string name, JsonElement el)
    {
        var command = Str(el, "command");
        var url = Str(el, "url");
        if (string.IsNullOrWhiteSpace(command) && string.IsNullOrWhiteSpace(url)) return null;

        var type = Str(el, "type");
        if (string.IsNullOrWhiteSpace(type))
            type = !string.IsNullOrWhiteSpace(url) ? "http" : "stdio";

        return new McpServerOptions
        {
            Name = name,
            Type = type,
            Command = command,
            Url = url,
            Arguments = StrList(el, "args", "arguments"),
            Env = Map(el, "env"),
        };
    }

    private static McpServerOptions? ParseArrayItem(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        // Acepta claves en PascalCase (formato app) o minúsculas (estándar).
        var name = Str(el, "name", "Name");
        if (string.IsNullOrWhiteSpace(name)) name = Str(el, "command", "Command", "url", "Url");
        var command = Str(el, "command", "Command");
        var url = Str(el, "url", "Url");
        var type = Str(el, "type", "Type");
        if (string.IsNullOrWhiteSpace(type))
            type = !string.IsNullOrWhiteSpace(url) ? "http" : "stdio";

        return new McpServerOptions
        {
            Name = string.IsNullOrWhiteSpace(name) ? "mcp" : name,
            Type = type,
            Command = command,
            Url = url,
            Arguments = StrList(el, "args", "arguments", "Arguments"),
            Env = Map(el, "env", "Env"),
        };
    }

    // ---- helpers de lectura tolerante ----------------------------------------

    private static string Str(JsonElement el, params string[] names)
    {
        foreach (var n in names)
            if (el.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? "";
        return "";
    }

    private static List<string> StrList(JsonElement el, params string[] names)
    {
        foreach (var n in names)
            if (el.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Array)
                return v.EnumerateArray()
                    .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.ToString())
                    .ToList();
        return new();
    }

    private static Dictionary<string, string> Map(JsonElement el, params string[] names)
    {
        foreach (var n in names)
            if (el.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Object)
                return v.EnumerateObject()
                    .ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString());
        return new();
    }
}
