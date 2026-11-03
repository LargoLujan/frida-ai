using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using TeamsJarvis.Core;

namespace TeamsJarvis.Tools;

/// <summary>
/// Cliente MCP: conecta con los servidores configurados en appsettings y expone
/// sus tools al agente como "mcp_{servidor}_{tool}". Un servidor caído no tumba la app.
/// </summary>
public sealed class McpToolBridge : IAsyncDisposable
{
    private readonly JarvisOptions _options;
    private readonly ILogger<McpToolBridge> _logger;
    private readonly List<McpClient> _clients = new();
    private readonly List<IAgentTool> _tools = new();
    private readonly Dictionary<string, string> _serverStatus = new();
    private bool _initialized;

    public McpToolBridge(IOptions<JarvisOptions> options, ILogger<McpToolBridge> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public IReadOnlyList<IAgentTool> Tools => _tools;
    public IReadOnlyDictionary<string, string> ServerStatus => _serverStatus;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        _initialized = true;

        foreach (var server in _options.McpServers)
        {
            try
            {
                // Tipo efectivo: si no se especifica, se infiere por si hay Url (http) o Command (stdio).
                var type = server.Type;
                if (string.IsNullOrWhiteSpace(type))
                    type = !string.IsNullOrWhiteSpace(server.Url) ? "http" : "stdio";

                IClientTransport transport = type.ToLowerInvariant() switch
                {
                    "stdio" => new StdioClientTransport(new StdioClientTransportOptions
                    {
                        Name = server.Name,
                        Command = server.Command,
                        Arguments = server.Arguments,
                        EnvironmentVariables = server.Env.Count > 0
                            ? server.Env.ToDictionary(kv => kv.Key, kv => (string?)kv.Value)
                            : null,
                    }),
                    "http" or "sse" => new HttpClientTransport(new HttpClientTransportOptions
                    {
                        Name = server.Name,
                        Endpoint = new Uri(server.Url),
                    }),
                    _ => throw new InvalidOperationException($"Tipo de servidor MCP desconocido: {type}"),
                };

                var client = await McpClient.CreateAsync(transport, cancellationToken: ct);
                _clients.Add(client);

                var tools = await client.ListToolsAsync(cancellationToken: ct);
                foreach (var tool in tools)
                    _tools.Add(new McpProxyTool(server.Name, client, tool));

                _serverStatus[server.Name] = $"OK ({tools.Count} tools)";
                _logger.LogInformation("MCP '{Server}' conectado con {Count} tools.", server.Name, tools.Count);
            }
            catch (Exception ex)
            {
                _serverStatus[server.Name] = $"ERROR: {ex.Message}";
                _logger.LogWarning(ex, "No se pudo conectar al servidor MCP '{Server}'.", server.Name);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            try { await client.DisposeAsync(); } catch { }
        }
    }

    /// <summary>Adaptador de una tool MCP a IAgentTool.</summary>
    private sealed class McpProxyTool : IAgentTool
    {
        private readonly McpClient _client;
        private readonly McpClientTool _tool;
        private readonly Dictionary<string, object> _schemaProperties = new();
        private readonly List<string> _required = new();

        public McpProxyTool(string serverName, McpClient client, McpClientTool tool)
        {
            _client = client;
            _tool = tool;
            Name = SanitizeName($"mcp_{serverName}_{tool.Name}");
            Description = $"[MCP:{serverName}] {tool.Description}";

            // Copiar el input schema del servidor MCP.
            if (tool.JsonSchema.ValueKind == JsonValueKind.Object)
            {
                if (tool.JsonSchema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in props.EnumerateObject())
                        _schemaProperties[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText())!;
                }
                if (tool.JsonSchema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in req.EnumerateArray())
                        if (item.GetString() is { } name) _required.Add(name);
                }
            }
        }

        public string Name { get; }
        public string Description { get; }
        public IReadOnlyDictionary<string, object> SchemaProperties => _schemaProperties;
        public IReadOnlyList<string> RequiredProperties => _required;

        public async Task<string> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> input, CancellationToken ct)
        {
            var arguments = input.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
            var result = await _client.CallToolAsync(_tool.Name, arguments, cancellationToken: ct);

            var sb = new StringBuilder();
            foreach (var block in result.Content)
            {
                if (block is ModelContextProtocol.Protocol.TextContentBlock text)
                    sb.AppendLine(text.Text);
                else
                    sb.AppendLine($"(contenido no textual: {block.Type})");
            }
            var output = sb.ToString().Trim();
            return string.IsNullOrEmpty(output) ? "(sin salida)" : output;
        }

        private static string SanitizeName(string name)
        {
            var sanitized = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
            return sanitized.Length > 64 ? sanitized[..64] : sanitized;
        }
    }
}
